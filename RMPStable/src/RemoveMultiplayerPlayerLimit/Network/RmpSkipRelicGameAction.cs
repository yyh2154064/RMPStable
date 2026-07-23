using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Network;

public class RmpSkipRelicGameAction : GameAction
{
	private readonly Player _player;

	public override ulong OwnerId => _player.NetId;

	public override GameActionType ActionType => GameActionType.NonCombat;

	public RmpSkipRelicGameAction(Player player)
	{
		_player = player;
	}

	protected override Task ExecuteAction()
	{
		RunManager.Instance.TreasureRoomRelicSynchronizer.OnPicked(_player, -1);
		return Task.CompletedTask;
	}

	public override INetAction ToNetAction()
	{
		return default(RmpSkipRelicNetAction);
	}

	public override string ToString()
	{
		return $"RmpSkipRelicAction for player {_player.NetId}";
	}
}
