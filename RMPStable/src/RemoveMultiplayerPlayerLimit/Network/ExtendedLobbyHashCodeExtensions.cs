namespace RemoveMultiplayerPlayerLimit.Network;

internal static class ExtendedLobbyHashCodeExtensions
{
	public static ulong GetHashCodeAsUlong(this object value)
	{
		return (ulong)value.GetHashCode();
	}
}
