using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.TreasureRoom;

public class TreasureModule : IRMPModule
{
	private class TreasureNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _EnterTree = "_EnterTree";

			public new static readonly StringName _ExitTree = "_ExitTree";

			public static readonly StringName OnNodeAdded = "OnNodeAdded";

			public new static readonly StringName _Process = "_Process";

			public static readonly StringName ExpandHolders = "ExpandHolders";

			public static readonly StringName BootstrapExtraHolders = "BootstrapExtraHolders";

			public static readonly StringName EnsureTreasureChestHandler = "EnsureTreasureChestHandler";

			public static readonly StringName UnregisterTreasureChestHandler = "UnregisterTreasureChestHandler";

			public static readonly StringName ApplyLayout = "ApplyLayout";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _lastCollection = "_lastCollection";

			public static readonly StringName _layoutApplied = "_layoutApplied";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly TreasureModule _mod;

		private int _frameCounter;

		private NTreasureRoomRelicCollection? _lastCollection;

		private bool _layoutApplied;

		private RunLocationTargetedMessageBuffer? _registeredMessageBuffer;

		private MessageHandlerDelegate<TreasureChestOpenedMessage>? _treasureChestOpenedHandler;

		private readonly HashSet<string> _mirroredRemoteChestRewards = new HashSet<string>();

		private TreasureRoomRelicSynchronizer? _diagnosticSynchronizer;

		private Action? _diagnosticVotesChangedHandler;

		private Action<List<RelicPickingResult>>? _diagnosticRelicsAwardedHandler;

		private readonly HashSet<ulong> _diagnosticHolderIds = new HashSet<ulong>();

		private readonly Dictionary<ulong, int> _diagnosticHolderReleaseCounts = new Dictionary<ulong, int>();

		private string? _lastTreasureDiagnosticSnapshot;

		public TreasureNode(TreasureModule mod)
		{
			_mod = mod;
			base.Name = "TreasureNode";
		}

		public override void _EnterTree()
		{
			GetTree().NodeAdded += OnNodeAdded;
		}

		public override void _ExitTree()
		{
			SceneTree tree = GetTree();
			if (tree != null)
			{
				tree.NodeAdded -= OnNodeAdded;
			}
			BindTreasureDiagnostics(null);
			UnregisterTreasureChestHandler();
		}

		private void OnNodeAdded(Node node)
		{
			if (!(node is NTreasureRoomRelicCollection collection))
			{
				return;
			}
			try
			{
				_mod.PrewarmAllPlayerStates();
			}
			catch (Exception ex)
			{
				Log.Warn("[RMP:Treasure] Pre-warm peer input states failed: " + ex.Message);
			}
			try
			{
				ExpandHolders(collection);
			}
			catch (Exception ex2)
			{
				Log.Warn("[RMP:Treasure] Pre-expand failed: " + ex2.Message);
			}
		}

		public override void _Process(double delta)
		{
			EnsureTreasureChestHandler();
			BindTreasureDiagnostics(RunManager.Instance?.TreasureRoomRelicSynchronizer);
			if (++_frameCounter % 10 != 0)
			{
				return;
			}
			NTreasureRoomRelicCollection nTreasureRoomRelicCollection = SceneMonitor.FindTreasureRoomRelicCollection();
			if (nTreasureRoomRelicCollection == null || nTreasureRoomRelicCollection != _lastCollection)
			{
				if (_lastCollection != null)
				{
					string previousInstance = GodotObject.IsInstanceValid(_lastCollection) ? _lastCollection.GetInstanceId().ToString() : "freed";
					Log.Info($"[RMP:TreasureDiag] Relic collection left (instance={previousInstance}).");
				}
				_lastCollection = nTreasureRoomRelicCollection;
				_layoutApplied = false;
				_diagnosticHolderIds.Clear();
				_diagnosticHolderReleaseCounts.Clear();
				_lastTreasureDiagnosticSnapshot = null;
				if (nTreasureRoomRelicCollection != null)
				{
					EnsureHolderDiagnosticHandlers(nTreasureRoomRelicCollection);
					LogTreasureState("collection-entered", force: true);
				}
			}
			else
			{
				if (nTreasureRoomRelicCollection == null)
				{
					return;
				}
				ExpandHolders(nTreasureRoomRelicCollection);
				_mod.PrewarmAllPlayerStates();
				EnsureHolderDiagnosticHandlers(nTreasureRoomRelicCollection);
				LogTreasureState("poll");
				List<NTreasureRoomRelicHolder> holdersInUse = _mod.GetHoldersInUse(nTreasureRoomRelicCollection);
				if (holdersInUse == null || holdersInUse.Count == 0)
				{
					return;
				}
				IReadOnlyList<RelicModel> readOnlyList = (RunManager.Instance?.TreasureRoomRelicSynchronizer)?.CurrentRelics;
				if (readOnlyList == null)
				{
					return;
				}
				try
				{
					if (!_layoutApplied && holdersInUse.Count < readOnlyList.Count)
					{
						BootstrapExtraHolders(nTreasureRoomRelicCollection);
					}
					holdersInUse = _mod.GetHoldersInUse(nTreasureRoomRelicCollection);
					if (holdersInUse != null && holdersInUse.Count >= readOnlyList.Count && !_layoutApplied)
					{
						ApplyLayout(nTreasureRoomRelicCollection);
						_layoutApplied = true;
					}
				}
				catch (Exception ex)
				{
					Log.Warn("[RMP:Treasure] Bootstrap/layout failed (will retry): " + ex.Message);
				}
			}
		}

		private void ExpandHolders(NTreasureRoomRelicCollection collection)
		{
			List<NTreasureRoomRelicHolder> multiplayerHolders = _mod.GetMultiplayerHolders(collection);
			if (multiplayerHolders == null || multiplayerHolders.Count == 0)
			{
				return;
			}
			IReadOnlyList<RelicModel> readOnlyList = RunManager.Instance?.TreasureRoomRelicSynchronizer?.CurrentRelics;
			if (readOnlyList == null || readOnlyList.Count <= multiplayerHolders.Count)
			{
				return;
			}
			NTreasureRoomRelicHolder nTreasureRoomRelicHolder = multiplayerHolders[multiplayerHolders.Count - 1];
			string sceneFilePath = nTreasureRoomRelicHolder.SceneFilePath;
			PackedScene packedScene = ((!string.IsNullOrEmpty(sceneFilePath)) ? PreloadManager.Cache.GetScene(sceneFilePath) : null);
			Node parent = nTreasureRoomRelicHolder.GetParent();
			for (int i = multiplayerHolders.Count; i < readOnlyList.Count; i++)
			{
				NTreasureRoomRelicHolder nTreasureRoomRelicHolder2 = ((packedScene != null) ? packedScene.Instantiate<NTreasureRoomRelicHolder>(PackedScene.GenEditState.Disabled) : (nTreasureRoomRelicHolder.Duplicate() as NTreasureRoomRelicHolder));
				if (nTreasureRoomRelicHolder2 != null)
				{
					nTreasureRoomRelicHolder2.Name = $"AutoHolder_{i + 1}";
					nTreasureRoomRelicHolder2.Visible = false;
					parent.AddChild(nTreasureRoomRelicHolder2, forceReadableName: false, InternalMode.Disabled);
					multiplayerHolders.Add(nTreasureRoomRelicHolder2);
				}
			}
		}

		private void BootstrapExtraHolders(NTreasureRoomRelicCollection collection)
		{
			List<NTreasureRoomRelicHolder> holdersInUse = _mod.GetHoldersInUse(collection);
			List<NTreasureRoomRelicHolder> multiplayerHolders = _mod.GetMultiplayerHolders(collection);
			IRunState runState = _mod.GetRunState(collection);
			IReadOnlyList<RelicModel> readOnlyList = RunManager.Instance?.TreasureRoomRelicSynchronizer?.CurrentRelics;
			if (holdersInUse == null || multiplayerHolders == null || runState == null || readOnlyList == null || readOnlyList.Count <= 4)
			{
				return;
			}
			for (int i = holdersInUse.Count; i < multiplayerHolders.Count && i < readOnlyList.Count; i++)
			{
				NTreasureRoomRelicHolder nTreasureRoomRelicHolder = multiplayerHolders[i];
				try
				{
					if (nTreasureRoomRelicHolder.Relic == null)
					{
						continue;
					}
					nTreasureRoomRelicHolder.Visible = true;
					nTreasureRoomRelicHolder.Relic.Model = readOnlyList[i];
					nTreasureRoomRelicHolder.Initialize(readOnlyList[i], runState);
					nTreasureRoomRelicHolder.Index = i;
					int idx = i;
					nTreasureRoomRelicHolder.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate
					{
						TreasureRoomRelicSynchronizer treasureRoomRelicSynchronizer = RunManager.Instance?.TreasureRoomRelicSynchronizer;
						if (treasureRoomRelicSynchronizer?.CurrentRelics != null)
						{
							treasureRoomRelicSynchronizer.PickRelicLocally(idx);
						}
					}));
					holdersInUse.Add(nTreasureRoomRelicHolder);
					nTreasureRoomRelicHolder.VoteContainer?.RefreshPlayerVotes();
				}
				catch (Exception ex)
				{
					Log.Warn($"[RMP:Treasure] Failed to bootstrap holder {i}: {ex.Message}");
				}
			}
			RebuildHolderFocusNavigation(holdersInUse);
		}

		private void EnsureTreasureChestHandler()
		{
		}

		private void UnregisterTreasureChestHandler()
		{
			if (_registeredMessageBuffer != null && _treasureChestOpenedHandler != null)
			{
				try
				{
					_registeredMessageBuffer.UnregisterMessageHandler(_treasureChestOpenedHandler);
				}
				catch
				{
				}
			}
			_registeredMessageBuffer = null;
			_treasureChestOpenedHandler = null;
			_mirroredRemoteChestRewards.Clear();
		}

		private void HandleTreasureChestOpened(TreasureChestOpenedMessage message, ulong senderId)
		{
		}

		private void BindTreasureDiagnostics(TreasureRoomRelicSynchronizer? synchronizer)
		{
			if (ReferenceEquals(_diagnosticSynchronizer, synchronizer))
			{
				return;
			}
			if (_diagnosticSynchronizer != null)
			{
				if (_diagnosticVotesChangedHandler != null)
				{
					_diagnosticSynchronizer.VotesChanged -= _diagnosticVotesChangedHandler;
				}
				if (_diagnosticRelicsAwardedHandler != null)
				{
					_diagnosticSynchronizer.RelicsAwarded -= _diagnosticRelicsAwardedHandler;
				}
			}

			_diagnosticSynchronizer = synchronizer;
			_diagnosticVotesChangedHandler = null;
			_diagnosticRelicsAwardedHandler = null;
			_lastTreasureDiagnosticSnapshot = null;
			if (synchronizer == null)
			{
				return;
			}

			_diagnosticVotesChangedHandler = () => LogTreasureState("votes-changed", force: true);
			_diagnosticRelicsAwardedHandler = results =>
			{
				StringBuilder summary = new StringBuilder();
				for (int i = 0; i < results.Count; i++)
				{
					RelicPickingResult result = results[i];
					if (i > 0)
					{
						summary.Append("; ");
					}
					summary.Append($"type={result.type}, player={result.player?.NetId.ToString() ?? "null"}, relic={result.relic?.ToString() ?? "none"}, fight={result.fight}");
				}
				Log.Info($"[RMP:TreasureDiag] Relics awarded (count={results.Count}): [{summary}]");
				LogTreasureState("relics-awarded", force: true);
			};
			synchronizer.VotesChanged += _diagnosticVotesChangedHandler;
			synchronizer.RelicsAwarded += _diagnosticRelicsAwardedHandler;
			Log.Info($"[RMP:TreasureDiag] Bound diagnostics to treasure synchronizer (localPlayer={_mod.GetSyncLocalPlayerId(synchronizer)?.ToString() ?? "unknown"}).");
		}

		private void EnsureHolderDiagnosticHandlers(NTreasureRoomRelicCollection collection)
		{
			List<NTreasureRoomRelicHolder> holders = _mod.GetHoldersInUse(collection);
			if (holders == null)
			{
				return;
			}
			foreach (NTreasureRoomRelicHolder holder in holders)
			{
				if (holder == null || !GodotObject.IsInstanceValid(holder))
				{
					continue;
				}
				ulong instanceId = holder.GetInstanceId();
				if (!_diagnosticHolderIds.Add(instanceId))
				{
					continue;
				}
				holder.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => LogHolderReleased(holder)));
				Log.Info($"[RMP:TreasureDiag] Observing relic holder release (collection={collection.GetInstanceId()}, holder={instanceId}, name={holder.Name}, index={holder.Index}).");
			}
		}

		private void LogHolderReleased(NTreasureRoomRelicHolder holder)
		{
			ulong instanceId = holder.GetInstanceId();
			int releaseCount = _diagnosticHolderReleaseCounts.TryGetValue(instanceId, out int currentCount) ? currentCount + 1 : 1;
			_diagnosticHolderReleaseCounts[instanceId] = releaseCount;
			TreasureRoomRelicSynchronizer? synchronizer = RunManager.Instance?.TreasureRoomRelicSynchronizer;
			List<RelicModel>? relics = synchronizer == null ? null : _mod.GetSyncCurrentRelics(synchronizer);
			string relic = relics != null && holder.Index >= 0 && holder.Index < relics.Count ? relics[holder.Index].ToString() : "none";
			Log.Info($"[RMP:TreasureDiag] Relic holder released (holder={instanceId}, name={holder.Name}, index={holder.Index}, releaseCount={releaseCount}, active={relics != null}, relic={relic}).");
			LogTreasureState("holder-released", force: true);
		}

		private void LogTreasureState(string reason, bool force = false)
		{
			TreasureRoomRelicSynchronizer? synchronizer = _diagnosticSynchronizer;
			if (synchronizer == null)
			{
				return;
			}

			List<RelicModel>? relics = _mod.GetSyncCurrentRelics(synchronizer);
			List<int?>? votes = _mod.GetSyncVotes(synchronizer);
			int? predictedVote = _mod.GetSyncPredictedVote(synchronizer);
			StringBuilder relicSummary = new StringBuilder();
			if (relics != null)
			{
				for (int i = 0; i < relics.Count; i++)
				{
					if (i > 0)
					{
						relicSummary.Append(", ");
					}
					relicSummary.Append($"{i}:{relics[i]}");
				}
			}

			StringBuilder voteSummary = new StringBuilder();
			IPlayerCollection? playerCollection = _mod.GetSyncPlayerCollection(synchronizer);
			int playerIndex = 0;
			if (playerCollection != null)
			{
				foreach (Player player in playerCollection.Players)
				{
					if (playerIndex > 0)
					{
						voteSummary.Append(", ");
					}
					int? vote = votes != null && playerIndex < votes.Count ? votes[playerIndex] : null;
					voteSummary.Append($"{player.NetId}:{(vote.HasValue ? vote.Value.ToString() : "none")}");
					playerIndex++;
				}
			}

			string collectionInstance = _lastCollection == null ? "none" : GodotObject.IsInstanceValid(_lastCollection) ? _lastCollection.GetInstanceId().ToString() : "freed";
			string snapshot = $"collection={collectionInstance}, active={relics != null}, relics=[{relicSummary}], votes=[{voteSummary}], predicted={(predictedVote.HasValue ? predictedVote.Value.ToString() : "none")}";
			if (!force && string.Equals(snapshot, _lastTreasureDiagnosticSnapshot, StringComparison.Ordinal))
			{
				return;
			}
			_lastTreasureDiagnosticSnapshot = snapshot;
			Log.Info($"[RMP:TreasureDiag] State changed ({reason}): {snapshot}");
		}

		private void ApplyLayout(NTreasureRoomRelicCollection collection)
		{
			List<NTreasureRoomRelicHolder> holdersInUse = _mod.GetHoldersInUse(collection);
			if (holdersInUse == null || holdersInUse.Count <= 4)
			{
				return;
			}
			float num = float.MaxValue;
			float num2 = float.MinValue;
			float num3 = float.MaxValue;
			float num4 = float.MinValue;
			for (int i = 0; i < 4 && i < holdersInUse.Count; i++)
			{
				Vector2 position = holdersInUse[i].Position;
				num = Math.Min(num, position.X);
				num2 = Math.Max(num2, position.X);
				num3 = Math.Min(num3, position.Y);
				num4 = Math.Max(num4, position.Y);
			}
			int count = holdersInUse.Count;
			int num5 = Math.Max(1, Math.Min(4, count));
			int num6 = (int)Math.Ceiling((float)count / (float)num5);
			float num7 = (num + num2) * 0.5f;
			float num8 = (num3 + num4) * 0.5f;
			float num9 = (num2 - num) / (float)Math.Max(1, num5 - 1);
			num9 = ((num9 > 0f) ? Math.Max(190f, num9) : 220f);
			float num10 = 0f;
			if (num6 > 1)
			{
				float num11 = 0f;
				Vector2 combinedMinimumSize = holdersInUse[0].GetCombinedMinimumSize();
				if (combinedMinimumSize.Y > 0f)
				{
					num11 = combinedMinimumSize.Y;
				}
				float val = ((num11 > 0f) ? (num11 * 1.05f) : 0f);
				num10 = Math.Max(120f, Math.Max(Math.Abs(num4 - num3), val));
				if (num10 * (float)(num6 - 1) > 640f)
				{
					num10 = 640f / (float)(num6 - 1);
				}
			}
			int num12 = 0;
			for (int j = 0; j < num6; j++)
			{
				int num13 = Math.Min(num5, count - num12);
				float y = num8 + ((float)j - (float)(num6 - 1) * 0.5f) * num10;
				float num14 = num7 - (float)(num13 - 1) * num9 * 0.5f;
				for (int k = 0; k < num13; k++)
				{
					holdersInUse[num12 + k].Position = new Vector2(num14 + (float)k * num9, y);
				}
				num12 += num13;
			}
			RebuildHolderFocusNavigation(holdersInUse);
		}

		private static void RebuildHolderFocusNavigation(List<NTreasureRoomRelicHolder> holdersInUse)
		{
			if (holdersInUse.Count != 0)
			{
				for (int i = 0; i < holdersInUse.Count; i++)
				{
					NTreasureRoomRelicHolder nTreasureRoomRelicHolder = holdersInUse[i];
					nTreasureRoomRelicHolder.SetFocusMode(Control.FocusModeEnum.All);
					nTreasureRoomRelicHolder.FocusNeighborTop = nTreasureRoomRelicHolder.GetPath();
					nTreasureRoomRelicHolder.FocusNeighborBottom = nTreasureRoomRelicHolder.GetPath();
					NodePath focusNeighborLeft = ((i > 0) ? holdersInUse[i - 1].GetPath() : holdersInUse[holdersInUse.Count - 1].GetPath());
					nTreasureRoomRelicHolder.FocusNeighborLeft = focusNeighborLeft;
					nTreasureRoomRelicHolder.FocusNeighborRight = ((i < holdersInUse.Count - 1) ? holdersInUse[i + 1].GetPath() : holdersInUse[0].GetPath());
				}
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(9)
			{
				new Godot.Bridge.MethodInfo(MethodName._EnterTree, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName._ExitTree, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName.OnNodeAdded, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.ExpandHolders, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "collection", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.BootstrapExtraHolders, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "collection", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.EnsureTreasureChestHandler, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName.UnregisterTreasureChestHandler, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName.ApplyLayout, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "collection", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName._EnterTree && args.Count == 0)
			{
				_EnterTree();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName._ExitTree && args.Count == 0)
			{
				_ExitTree();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnNodeAdded && args.Count == 1)
			{
				OnNodeAdded(VariantUtils.ConvertTo<Node>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName._Process && args.Count == 1)
			{
				_Process(VariantUtils.ConvertTo<double>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.ExpandHolders && args.Count == 1)
			{
				ExpandHolders(VariantUtils.ConvertTo<NTreasureRoomRelicCollection>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.BootstrapExtraHolders && args.Count == 1)
			{
				BootstrapExtraHolders(VariantUtils.ConvertTo<NTreasureRoomRelicCollection>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.EnsureTreasureChestHandler && args.Count == 0)
			{
				EnsureTreasureChestHandler();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.UnregisterTreasureChestHandler && args.Count == 0)
			{
				UnregisterTreasureChestHandler();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.ApplyLayout && args.Count == 1)
			{
				ApplyLayout(VariantUtils.ConvertTo<NTreasureRoomRelicCollection>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._EnterTree)
			{
				return true;
			}
			if (method == MethodName._ExitTree)
			{
				return true;
			}
			if (method == MethodName.OnNodeAdded)
			{
				return true;
			}
			if (method == MethodName._Process)
			{
				return true;
			}
			if (method == MethodName.ExpandHolders)
			{
				return true;
			}
			if (method == MethodName.BootstrapExtraHolders)
			{
				return true;
			}
			if (method == MethodName.EnsureTreasureChestHandler)
			{
				return true;
			}
			if (method == MethodName.UnregisterTreasureChestHandler)
			{
				return true;
			}
			if (method == MethodName.ApplyLayout)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				_frameCounter = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			if (name == PropertyName._lastCollection)
			{
				_lastCollection = VariantUtils.ConvertTo<NTreasureRoomRelicCollection>(in value);
				return true;
			}
			if (name == PropertyName._layoutApplied)
			{
				_layoutApplied = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
			return base.SetGodotClassPropertyValue(in name, in value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				value = VariantUtils.CreateFrom(in _frameCounter);
				return true;
			}
			if (name == PropertyName._lastCollection)
			{
				value = VariantUtils.CreateFrom(in _lastCollection);
				return true;
			}
			if (name == PropertyName._layoutApplied)
			{
				value = VariantUtils.CreateFrom(in _layoutApplied);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.PropertyInfo> GetGodotPropertyList()
		{
			return new List<Godot.Bridge.PropertyInfo>
			{
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Object, PropertyName._lastCollection, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._layoutApplied, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._lastCollection, Variant.From(in _lastCollection));
			info.AddProperty(PropertyName._layoutApplied, Variant.From(in _layoutApplied));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
			if (info.TryGetProperty(PropertyName._lastCollection, out var value2))
			{
				_lastCollection = value2.As<NTreasureRoomRelicCollection>();
			}
			if (info.TryGetProperty(PropertyName._layoutApplied, out var value3))
			{
				_layoutApplied = value3.As<bool>();
			}
		}
	}

	private const float FallbackXStep = 220f;

	private const float MinXStep = 190f;

	private const float MinYStep = 120f;

	private ReflectionCache _cache;

	internal FieldInfo? HoldersInUseField;

	internal FieldInfo? MultiplayerHoldersField;

	internal FieldInfo? RunStateField;

	internal FieldInfo? SyncPlayerCollectionField;

	internal FieldInfo? SyncLocalPlayerIdField;

	internal FieldInfo? SyncActionQueueField;

	internal FieldInfo? SyncCurrentRelicsField;

	internal FieldInfo? SyncRngField;

	internal FieldInfo? SyncVotesField;

	internal FieldInfo? SyncPredictedVoteField;

	internal FieldInfo? VotesChangedEventField;

	internal FieldInfo? RelicsAwardedEventField;

	internal System.Reflection.MethodInfo? EndRelicVotingMethod;

	internal System.Reflection.MethodInfo? PeerInputGetOrCreateMethod;

	internal readonly HashSet<TreasureRoomRelicSynchronizer> LocalVotePending = new HashSet<TreasureRoomRelicSynchronizer>();

	internal readonly HashSet<TreasureRoomRelicSynchronizer> LocalSkipLocked = new HashSet<TreasureRoomRelicSynchronizer>();

	public string Name => "TreasureRoom";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_cache = cache;
		Type typeFromHandle = typeof(NTreasureRoomRelicCollection);
		Type typeFromHandle2 = typeof(TreasureRoomRelicSynchronizer);
		HoldersInUseField = cache.GetField(typeFromHandle, "_holdersInUse");
		MultiplayerHoldersField = cache.GetField(typeFromHandle, "_multiplayerHolders");
		RunStateField = cache.GetField(typeFromHandle, "_runState");
		SyncPlayerCollectionField = cache.GetField(typeFromHandle2, "_playerCollection");
		SyncLocalPlayerIdField = cache.GetField(typeFromHandle2, "_localPlayerId");
		SyncActionQueueField = cache.GetField(typeFromHandle2, "_actionQueueSynchronizer");
		SyncCurrentRelicsField = cache.GetField(typeFromHandle2, "_currentRelics");
		SyncRngField = cache.GetField(typeFromHandle2, "_rng");
		SyncVotesField = cache.GetField(typeFromHandle2, "_votes");
		SyncPredictedVoteField = cache.GetField(typeFromHandle2, "_predictedVote");
		VotesChangedEventField = cache.GetField(typeFromHandle2, "VotesChanged");
		RelicsAwardedEventField = cache.GetField(typeFromHandle2, "RelicsAwarded");
		EndRelicVotingMethod = cache.GetMethod(typeFromHandle2, "EndRelicVoting");
		PeerInputGetOrCreateMethod = typeof(PeerInputSynchronizer).GetMethod("GetOrCreateStateForPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
	}

	internal void PrewarmAllPlayerStates()
	{
		if (PeerInputGetOrCreateMethod == null)
		{
			return;
		}
		PeerInputSynchronizer peerInputSynchronizer = RunManager.Instance?.InputSynchronizer;
		if (peerInputSynchronizer == null)
		{
			return;
		}
		RunState runState = GameStateAccessor.GetRunState();
		if (runState?.Players == null)
		{
			return;
		}
		foreach (Player player in runState.Players)
		{
			try
			{
				PeerInputGetOrCreateMethod.Invoke(peerInputSynchronizer, new object[1] { player.NetId });
			}
			catch
			{
			}
		}
	}

	public Node? CreateNode()
	{
		return new TreasureNode(this);
	}

	public void Cleanup()
	{
		LocalVotePending.Clear();
		LocalSkipLocked.Clear();
	}

	internal List<NTreasureRoomRelicHolder>? GetHoldersInUse(NTreasureRoomRelicCollection c)
	{
		return HoldersInUseField?.GetValue(c) as List<NTreasureRoomRelicHolder>;
	}

	internal List<NTreasureRoomRelicHolder>? GetMultiplayerHolders(NTreasureRoomRelicCollection c)
	{
		return MultiplayerHoldersField?.GetValue(c) as List<NTreasureRoomRelicHolder>;
	}

	internal IRunState? GetRunState(NTreasureRoomRelicCollection c)
	{
		return RunStateField?.GetValue(c) as IRunState;
	}

	internal IPlayerCollection? GetSyncPlayerCollection(TreasureRoomRelicSynchronizer s)
	{
		return SyncPlayerCollectionField?.GetValue(s) as IPlayerCollection;
	}

	internal ulong? GetSyncLocalPlayerId(TreasureRoomRelicSynchronizer s)
	{
		object obj = SyncLocalPlayerIdField?.GetValue(s);
		if (!(obj is ulong))
		{
			return null;
		}
		return (ulong)obj;
	}

	internal ActionQueueSynchronizer? GetSyncActionQueue(TreasureRoomRelicSynchronizer s)
	{
		return SyncActionQueueField?.GetValue(s) as ActionQueueSynchronizer;
	}

	internal List<RelicModel>? GetSyncCurrentRelics(TreasureRoomRelicSynchronizer s)
	{
		return SyncCurrentRelicsField?.GetValue(s) as List<RelicModel>;
	}

	internal Rng? GetSyncRng(TreasureRoomRelicSynchronizer s)
	{
		return SyncRngField?.GetValue(s) as Rng;
	}

	internal List<int?>? GetSyncVotes(TreasureRoomRelicSynchronizer s)
	{
		return SyncVotesField?.GetValue(s) as List<int?>;
	}

	internal int? GetSyncPredictedVote(TreasureRoomRelicSynchronizer s)
	{
		object? value = SyncPredictedVoteField?.GetValue(s);
		if (value is int vote)
		{
			return vote;
		}
		return null;
	}

	internal void SetSyncPredictedVote(TreasureRoomRelicSynchronizer s, int? vote)
	{
		if (!(SyncPredictedVoteField == null))
		{
			Type fieldType = SyncPredictedVoteField.FieldType;
			if (fieldType == typeof(int?))
			{
				SyncPredictedVoteField.SetValue(s, vote);
			}
			else if (fieldType == typeof(int))
			{
				SyncPredictedVoteField.SetValue(s, vote.GetValueOrDefault(-1));
			}
		}
	}

	internal void InvokeVotesChanged(TreasureRoomRelicSynchronizer s)
	{
		if (VotesChangedEventField?.GetValue(s) is Action action)
		{
			action();
		}
	}

	internal void InvokeRelicsAwarded(TreasureRoomRelicSynchronizer s, List<RelicPickingResult> results)
	{
		if (RelicsAwardedEventField?.GetValue(s) is Action<List<RelicPickingResult>> action)
		{
			action(results);
		}
	}

	internal void InvokeEndRelicVoting(TreasureRoomRelicSynchronizer s)
	{
		EndRelicVotingMethod?.Invoke(s, null);
	}

	internal void ClearLocalVoteState(TreasureRoomRelicSynchronizer s)
	{
		LocalVotePending.Remove(s);
		LocalSkipLocked.Remove(s);
		SetSyncPredictedVote(s, null);
	}
}
