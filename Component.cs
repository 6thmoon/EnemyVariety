using System.Reflection.Emit;

namespace Local.Enemy.Variety;

class Override : MonoBehaviour
{
	internal static void Set(CombatDirector director, Type value)
	{
		var instance = director.GetComponent<Override>();

		if ( value is Type.None )
		{
			if ( instance ) Destroy(instance);
			else return;
		}
		else instance ??= director.gameObject.AddComponent<Override>();

		instance.value = value;
	}

	internal static Type Get(CombatDirector director)
	{
		return director.TryGetComponent(out Override instance) ? instance.value : Type.None;
	}

	internal Type value;

	public static void Clear(CombatDirector director)
	{
		ref bool started = ref director.hasStartedWave;

		if ( started ) Override.Set(director, Type.None);
		started = false;
	}
}

enum Type
{
	None = default,
	Scene,
	Boss,
	Shrine,
	Card
}

class Hook
{
	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.Simulate))]
	[HarmonyTranspiler]
	static IEnumerable<CodeInstruction> TriggerWaveStart(IEnumerable<CodeInstruction> IL)
	{
		var type = typeof(CombatDirector);
		FieldInfo indicator = type.GetField(nameof(CombatDirector.hasStartedWave),
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
			), configuration = type.GetField(nameof(CombatDirector.shouldSpawnOneWave));

		foreach ( CodeInstruction instruction in IL )
		{
			yield return instruction;

			if ( instruction.LoadsField(configuration) )
			{
				yield return new(OpCodes.Pop);
				yield return new(OpCodes.Ldc_I4_1);
			}
			else if ( instruction.LoadsField(indicator) )
			{
				yield return new(OpCodes.Ldarg_0);
				yield return new(OpCodes.Ldfld, configuration);
				yield return new(OpCodes.And);
				yield return new(OpCodes.Ldarg_0);
				yield return new(OpCodes.Call,
						typeof(Override).GetMethod(nameof(Override.Clear)));
			}
		}
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.SpendAllCreditsOnMapSpawns))]
	[HarmonyPrefix]
	static void PopulateScene(CombatDirector __instance)
	{
		__instance.hasStartedWave = true;
		Override.Set(__instance, Type.Scene);
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.SpendAllCreditsOnMapSpawns))]
	[HarmonyPostfix]
	static void EndScene(CombatDirector __instance)
			=> Override.Clear(__instance);

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.SetNextSpawnAsBoss))]
	[HarmonyPrefix]
	static void SetBoss(CombatDirector __instance)
			=> Override.Set(__instance, Type.Boss);

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.CombatShrineActivation))]
	[HarmonyPostfix]
	static void CombatShrine(CombatDirector __instance)
			=> Override.Set(__instance, Type.Shrine);

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.OverrideCurrentMonsterCard))]
	[HarmonyPrefix]
	static void OverrideMonsterCard(CombatDirector __instance)
			=> Override.Set(__instance, Type.Card);
}
