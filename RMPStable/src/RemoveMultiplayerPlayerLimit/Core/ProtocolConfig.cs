namespace RemoveMultiplayerPlayerLimit.Core;

internal static class ProtocolConfig
{
	internal const int MaxPlayerLimit = 16;

	internal const int TargetPlayerLimit = 16;

	internal const int VanillaSlotIdBits = 2;

	internal const int VanillaLobbyListLengthBits = 3;

	internal const int OfficialSerializableLobbyLimit = 7;

	internal const int SlotIdBits = 4;

	internal const int LobbyListLengthBits = 5;

	internal static bool DifficultyScalingEnabled { get; private set; } = true;


	internal static void SetDifficultyScalingEnabled(bool value)
	{
		DifficultyScalingEnabled = value;
	}
}
