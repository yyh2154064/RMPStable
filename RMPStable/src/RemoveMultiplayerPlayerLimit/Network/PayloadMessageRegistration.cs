using System;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace RemoveMultiplayerPlayerLimit.Network;

internal static class PayloadMessageRegistration
{
	private static readonly FieldInfo? ModTypesField = typeof(ReflectionHelper).GetField("_modTypes", BindingFlags.Static | BindingFlags.NonPublic);
	private static bool _registered;

	internal static void EnsureRegistered()
	{
		if (_registered)
		{
			return;
		}
		if (ModTypesField == null)
		{
			return;
		}

		Type[] payloadTypes = typeof(PayloadMessageRegistration).Assembly.GetTypes();
		Type[] modTypes = ReflectionHelper.ModTypes;
		if (!modTypes.Contains(typeof(RmpQuickSlBeginMessage)))
		{
			ModTypesField.SetValue(null, modTypes.Concat(payloadTypes).Distinct().ToArray());
		}

		// The game discovers mod messages from ReflectionHelper.ModTypes. The
		// version-specific payload is loaded from a stream, so it is absent from
		// the normal mod assembly scan unless we add its types explicitly.
		MessageTypes.Initialize();
		MessageTypes.TypeToId<RmpConfigSyncMessage>();
		MessageTypes.TypeToId<RmpLobbySnapshotMessage>();
		MessageTypes.TypeToId<RmpExtendedReadyStateMessage>();
		MessageTypes.TypeToId<RmpExtendedBeginRunMessage>();
		MessageTypes.TypeToId<RmpSyncRequestMessage>();
		MessageTypes.TypeToId<RmpMirrorPacket>();
		MessageTypes.TypeToId<RmpQuickSlRequestMessage>();
		MessageTypes.TypeToId<RmpQuickSlDecisionMessage>();
		MessageTypes.TypeToId<RmpQuickSlBeginMessage>();
		_registered = true;
		Log.Info("[RMP] Embedded payload network messages registered.");
	}

#if STS2_0113
	internal static void AttachPayloadDuringModLoad()
	{
		// ModManager marks this mod Loaded only after its initializer returns.
		// Populate its assembly list now so the game's initial maps see the payload.
		var mod = MegaCrit.Sts2.Core.Modding.ModManager.Mods.Single(entry => entry.manifest?.id == "RMPStable" && entry.state == MegaCrit.Sts2.Core.Modding.ModLoadState.None);
		mod.assemblies.Add(typeof(PayloadMessageRegistration).Assembly);
	}
#endif
}
