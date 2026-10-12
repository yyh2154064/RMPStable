using System;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static ulong catchupNext;
    static void ObserveCatchup(object renderer)
    {
        if (System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE") is not ("fourStages" or "battleSwitch19") || Time.GetTicksMsec() < catchupNext) return;
        catchupNext = Time.GetTicksMsec() + 100;
        var manager = RunManager.Instance;
        File.AppendAllText(Path.Combine(Shared,Role+"-catchup.jsonl"),JsonSerializer.Serialize(new {
            time=Time.GetTicksMsec(),busy=Get(renderer,"_busy"),scene=Get(renderer,"_sceneReady"),
            fast=Get(null,"Replica",Rmp("MirrorFastRestore")),generation=Get(renderer,"_generation"),events=Get(renderer,"_events"),
            running=manager.ActionExecutor.IsRunning,paused=manager.ActionExecutor.IsPaused,
            action=manager.ActionExecutor.CurrentlyRunningAction?.ToString(),starting=CombatManager.Instance.IsStarting,
            phase1=CombatManager.Instance.EndingPlayerTurnPhaseOne,phase2=CombatManager.Instance.EndingPlayerTurnPhaseTwo,
            fps=Engine.GetFramesPerSecond(),cap=Engine.MaxFps,process=Performance.GetMonitor(Performance.Monitor.TimeProcess)
        })+"\n");
    }
}
