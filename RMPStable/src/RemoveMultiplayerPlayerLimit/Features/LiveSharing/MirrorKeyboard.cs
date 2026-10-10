using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private readonly ConcurrentQueue<(uint Message, uint Key, long Data, long Generation, long Epoch)> _keyboard = new();
    private bool _keyboardCard;
    private readonly System.Collections.Generic.HashSet<uint> _pressedKeys = new();
    private static Key NativeKey(uint key) => key switch
    {
        8 => Key.Backspace, 9 => Key.Tab, 13 => Key.Enter, 16 => Key.Shift, 17 => Key.Ctrl, 18 => Key.Alt, 27 => Key.Escape,
        33 => Key.Pageup, 34 => Key.Pagedown, 35 => Key.End, 36 => Key.Home, 37 => Key.Left, 38 => Key.Up, 39 => Key.Right, 40 => Key.Down,
        45 => Key.Insert, 46 => Key.Delete,
        >= 0x70 and <= 0x87 => Key.F1 + (int)(key - 0x70),
        0xBA => (Key)';', 0xBB => (Key)'=', 0xBC => (Key)',', 0xBD => (Key)'-', 0xBE => (Key)'.', 0xBF => (Key)'/',
        0xC0 => (Key)'`', 0xDB => (Key)'[', 0xDC => (Key)'\\', 0xDD => (Key)']', 0xDE => (Key)'\'',
        _ => (Key)key
    };
    private void ProcessKeyboard()
    {
        while (_keyboard.TryDequeue(out var input))
        {
            bool pressed = input.Message is MirrorWin32.KeyDown or MirrorWin32.SysKeyDown;
            bool released = input.Message is MirrorWin32.KeyUp or MirrorWin32.SysKeyUp;
            if (released) _pressedKeys.Remove(input.Key);
            if (input.Generation != _desiredGeneration || input.Epoch != _epoch || !_sceneReady) continue;
            if (pressed) _pressedKeys.Add(input.Key);
            using var key = new InputEventKey { Keycode = NativeKey(input.Key), PhysicalKeycode = NativeKey(input.Key), Pressed = pressed, Echo = pressed && (input.Data & (1L << 30)) != 0,
                ShiftPressed = _pressedKeys.Contains(16) || _pressedKeys.Contains(0xA0) || _pressedKeys.Contains(0xA1),
                CtrlPressed = _pressedKeys.Contains(17) || _pressedKeys.Contains(0xA2) || _pressedKeys.Contains(0xA3),
                AltPressed = _pressedKeys.Contains(18) || _pressedKeys.Contains(0xA4) || _pressedKeys.Contains(0xA5) };
            if (UiOnlyOpen)
            {
                if (pressed && !key.Echo && (key.Keycode == Key.Escape || MatchesShortcut(key, MegaInput.pauseAndBack) || MatchesShortcut(key, MegaInput.cancel)))
                {
                    LocalSpectatorSource.Descendants<NBackButton>(SceneMonitor.FindSettingsScreen()!).FirstOrDefault(b => b.IsVisibleInTree() && b.IsEnabled)?.ForceClick();
                    continue;
                }
                if (input.Message == MirrorWin32.Char) { key.Keycode = Key.None; key.PhysicalKeycode = Key.None; key.Unicode = input.Key; key.Pressed = true; }
                if (pressed || released || input.Message == MirrorWin32.Char) NGame.Instance.GetViewport().PushInput(key, true);
                continue;
            }
            if (!pressed || key.Echo) continue;
            if (Diagnostic) GD.Print("[RMP:Mirror:Keyboard] key=" + key.Keycode + " control=" + _control + " waiting=" + _waitingForAuthority + " idle=" + NativeIdle + " cards=" + CanBufferCard + " handMode=" + NPlayerHand.Instance?.CurrentMode);
            bool Match(string action) => MatchesShortcut(key, action);
            bool confirm = key.Keycode == Key.Enter || Match("ui_confirm") || Match("ui_accept") || Match(MegaInput.select);
            if (QuickSl.QuickSlController.ConfirmationOpen)
            {
                var popup = NModalContainer.Instance?.OpenModal as Node;
                string button = key.Keycode == Key.Escape || Match(MegaInput.cancel) ? "NoButton" : confirm ? "YesButton" : "";
                if (popup != null && button.Length > 0) LocalSpectatorSource.Descendants<NClickableControl>(popup).FirstOrDefault(b => b.Name == button && b.IsEnabled)?.ForceClick();
                continue;
            }
            if (Match("rmpQuickSl")) { RequestKeyboardSl(); continue; }
            if (key.Keycode == Key.Escape || Match(MegaInput.pauseAndBack) || Match(MegaInput.cancel))
            {
                if (_dragCard != null) { CancelDrag(); continue; }
                if (NGame.Instance?.InspectCardScreen?.IsVisibleInTree() == true) NGame.Instance.InspectCardScreen.Close();
                else if (NGame.Instance?.InspectRelicScreen?.IsVisibleInTree() == true) NGame.Instance.InspectRelicScreen.Close();
                else if (PresentationScrollRoot is { } browse)
                    LocalSpectatorSource.Descendants<NBackButton>(browse).FirstOrDefault(b => b.IsEnabled)?.ForceClick();
                else if (NCapstoneContainer.Instance?.CurrentCapstoneScreen != null) NCapstoneContainer.Instance.Close();
                else if (NTargetManager.Instance?.IsInSelection == true) SendShortcutAction(a => a.Kind == "cancel");
                else if (LocalSpectatorSource.PendingNativeChoice) SendShortcutAction(a => a.Kind == "cancel" || MirrorNativeUi.Resolve(a.NativePath) is Control node && HasShortcut(node,key));
                else if (!SendShortcutAction(a => a.Kind == "cancel")) NRun.Instance?.GlobalUi.TopBar.Pause.ForceClick();
                continue;
            }
            var local = new NClickableControl?[] { NRun.Instance?.GlobalUi.TopBar.Deck } .Concat(NCombatRoom.Instance == null ? Enumerable.Empty<NClickableControl>() :
                LocalSpectatorSource.Descendants<NClickableControl>(NCombatRoom.Instance).Where(b => b.GetType().Name is "NDrawPileButton" or "NDiscardPileButton" or "NExhaustPileButton"));
            var localButton = local.FirstOrDefault(b => b != null && b.IsEnabled && HasShortcut(b, key));
            if (localButton != null && NOverlayStack.Instance?.Peek() == null && _dragCard == null) { localButton.ForceClick(); continue; }
            if (_keyboardCard && _dragCard != null)
            {
                if (Match("mega_release_card")) { CancelDrag(); continue; }
                if (key.Keycode is Key.Left or Key.Right or Key.Up or Key.Down)
                {
                    var hits = CaptureHits().Where(h => h.Kind == "target" && _state!.Players[0].Creature.CombatState is { } combat &&
                        h.Index >= 0 && h.Index < combat.Creatures.Count && _dragCard.CanPlayTargeting(combat.Creatures[h.Index])).ToArray();
                    if (hits.Length > 0)
                    {
                        int current = Array.FindIndex(hits, h => new Rect2(h.Rect[0],h.Rect[1],h.Rect[2],h.Rect[3]).HasPoint(_pointer));
                        var next = hits[(current + (key.Keycode is Key.Left or Key.Up ? hits.Length - 1 : 1) + hits.Length) % hits.Length].Rect;
                        _pointer = new Vector2(next[0]+next[2]/2, next[1]+next[3]/2);
                    }
                }
                else if (confirm)
                {
                    if (NTargetManager.Instance?.IsInSelection == true)
                    { using var select = new InputEventAction { Action = MegaInput.select, Pressed = true }; NTargetManager.Instance._Input(select); }
                    else
                    { using var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = NativePointer, GlobalPosition = NativePointer }; _nativePlay?._Input(click); }
                }
                continue;
            }
            for(int index = 0; index < 10; index++)
            {
                if (!Match("mega_select_card_" + (index+1))) continue;
                var holder = NPlayerHand.Instance?.ActiveHolders.ElementAtOrDefault(index);
                if (Diagnostic) GD.Print("[RMP:Mirror:Keyboard] select=" + index + " holder=" + holder?.CardModel?.Id + " playable=" + holder?.CardModel?.CanPlayTargeting(null));
                if (holder == null || !_control) break;
                if (NPlayerHand.Instance!.CurrentMode == NPlayerHand.Mode.Play && CanBufferCard && NativeIdle && !_waitingForAuthority)
                {
                    CancelDrag(); _dragCard = holder.CardModel; _dragGeneration = _generation; _dragTurn = _state!.Players[0].PlayerCombatState!.TurnNumber;
                    _pointer = _dragStart = new Vector2(.5f,.35f); _dragReleased = true; _keyboardCard = true;
                    typeof(NPlayerHand).GetMethod("StartCardPlay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(NPlayerHand.Instance, new object[]{holder,true});
                    if (_dragCard != null) _nativePlay = typeof(NPlayerHand).GetField("_currentCardPlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(NPlayerHand.Instance) as NCardPlay;
                    if (Diagnostic) GD.Print("[RMP:Mirror:Keyboard] started=" + _nativePlay?.GetType().Name + " retained=" + (_dragCard != null));
                }
                else SendShortcutAction(a => a.Kind == "select" && a.NativePath == MirrorNativeUi.Key(holder));
                break;
            }
            if (_dragCard != null) continue;
            if (!PresentationOpen && NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.Play && Match("ui_end_turn"))
            { SendShortcutAction(a => a.Kind == "endTurn"); continue; }
            var focused = NGame.Instance.GetViewport().GuiGetFocusOwner();
            if (key.Keycode is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down)
            {
                if (focused == null && LocalSpectatorSource.ActiveNativeUi is { } root)
                    focused = root.GetType().GetProperty("DefaultFocusedControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(root) as Control;
                var next = key.Keycode == Key.Tab ? focused?.FindNextValidFocus() : focused?.FindValidFocusNeighbor(key.Keycode switch { Key.Left => Side.Left, Key.Right => Side.Right, Key.Up => Side.Top, _ => Side.Bottom });
                (next ?? focused)?.GrabFocus(); continue;
            }
            if (focused != null && confirm)
            {
                if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is Node pauseRoot &&
                    LocalSpectatorSource.Descendants<NClickableControl>(pauseRoot).Any(b => b == focused && b.IsEnabled && b.Name.ToString() is "Settings" or "Resume"))
                    ((NClickableControl)focused).ForceClick();
                else SendShortcutAction(a => a.NativePath == MirrorNativeUi.Key(MirrorNativeUi.Parent<NCardHolder>(focused) ?? focused));
            }
            else SendShortcutAction(a => a.Kind == "endTurn" && Match("ui_accept") || MirrorNativeUi.Resolve(a.NativePath) is Control node && HasShortcut(node,key));
        }
    }
    private static bool MatchesShortcut(InputEventKey key, string action)
    {
        if (InputMap.HasAction(action) && key.IsActionPressed(action)) return true;
        if (NInputManager.Instance == null || !key.Pressed || key.Echo) return false;
#if STS2_0111
        var binding = NInputManager.Instance.GetMKbHotkey(action);
#else
        var binding = NInputManager.Instance.GetShortcutKey(action);
#endif
        return binding != Key.None && binding == key.Keycode;
    }
    private static bool HasShortcut(Control node, InputEventKey key) => node.GetType().GetProperty("Hotkeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(node) is string[] shortcuts && shortcuts.Any(s => MatchesShortcut(key,s));
    private bool SendShortcutAction(Func<ControlActionSnapshot,bool> predicate)
    {
        if (!_control || !NativeIdle || _waitingForAuthority || _state == null) return false;
        var action = _commands.CaptureCommands(_state).Control.Actions.LastOrDefault(a => a.Enabled && predicate(a));
        if (action == null) return false;
        SendIntent(action.Kind, action.NativeIndex, -1, MirrorState.Hash(_state), _events, action.NativePath); return true;
    }
    private void RequestKeyboardSl()
    {
        if (!_control || !NativeIdle || _waitingForAuthority || _state == null) return;
        QuickSl.QuickSlController.ShowReplicaConfirmation(() =>
        { if (_state != null) SendIntent("menu", -1, -1, MirrorState.Hash(_state), _events, value: "RmpQuickSlConfirmed"); });
    }
}
