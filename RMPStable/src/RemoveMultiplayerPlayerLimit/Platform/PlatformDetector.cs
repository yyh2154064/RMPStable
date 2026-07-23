using System;
using System.Runtime.InteropServices;

namespace RemoveMultiplayerPlayerLimit.Platform;

public static class PlatformDetector
{
	public static bool IsMacOS => OperatingSystem.IsMacOS();

	public static bool IsLinux => OperatingSystem.IsLinux();

	public static bool IsWindows => OperatingSystem.IsWindows();

	public static bool IsMacOSArm64
	{
		get
		{
			if (IsMacOS)
			{
				return RuntimeInformation.OSArchitecture == Architecture.Arm64;
			}
			return false;
		}
	}
}
