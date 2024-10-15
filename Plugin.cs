using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Permissions;
using System.Linq;
using UnityEngine;
using static UnityEngine.AddressableAssets.Addressables;

[assembly: AssemblyVersion(Local.Enemy.Variety.Plugin.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace Local.Enemy.Variety;

[BepInPlugin(identifier, "EnemyVariety", version)]
class Plugin : BaseUnityPlugin
{
	public const string version = "0.2.0", identifier = "local.enemy.variety";
	static ConfigEntry<bool> boss;

	protected async void Awake()
	{
		Harmony.CreateAndPatchAll(typeof(Plugin));
		boss = Config.Bind(
				section: "General", key: "Apply to Teleporter Boss",
				defaultValue: true, description:
					"If enabled, multiple boss types may appear during the teleporter event."
			);

		var obj = await LoadAssetAsync<GameObject>("RoR2/DLC2/ShrineHalcyonite.prefab").Task;
		foreach ( var director in obj.GetComponentsInChildren<CombatDirector>() )
			director.resetMonsterCardIfFailed = false;
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.AttemptSpawnOnTarget))]
	[HarmonyPrefix]
	static void ResetMonsterCard(CombatDirector __instance, ref bool __state)
	{
		ref DirectorCard card = ref __instance.currentMonsterCard;
		WeightedSelection<DirectorCard> selection = __instance.finalMonsterCardsSelection;

		__state = false;
		if ( ! __instance.resetMonsterCardIfFailed || card == null || selection == null  )
			return;

		foreach ( WeightedSelection<DirectorCard>.ChoiceInfo choice in selection.choices )
		{
			if ( ! object.Equals(card, choice.value) || choice.weight <= 0 || card.cost <= 0 )
				continue;

			int count = __instance.spawnCountInCurrentWave, previous = card.cost;
			do
			{
				if ( __instance == TeleporterInteraction.instance?.bossDirector )
				{
					if ( boss.Value )
					{
						__instance.SetNextSpawnAsBoss();
						__state = count is 0 || card.cost <= __instance.monsterCredit;
					}
					else break;
				}
				else
				{
					Xoroshiro128Plus rng = __instance.rng;

					do card = selection.Evaluate(rng.nextNormalizedFloat);
					while ( card.cost < rng.nextNormalizedFloat * __instance.monsterCredit );

					__instance.PrepareNewMonsterWave(card);
				}

			}
			while ( card.cost > previous && card.cost > __instance.monsterCredit );

			__instance.spawnCountInCurrentWave = count;
			break;
		}
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.AttemptSpawnOnTarget))]
	[HarmonyPostfix]
	static void RetryIfNodePlacementFailed(bool __state, ref bool __result)
	{
		__result |= __state;
	}

	[HarmonyPrefix, HarmonyPatch(typeof(Chat),
			nameof(Chat.SendBroadcastChat), [ typeof(ChatMessageBase) ])]
	static void ChangeMessage(ChatMessageBase message)
	{
		if ( message is Chat.SubjectFormatChatMessage chat && chat.paramTokens?.Any() is true
				&& chat.baseToken is "SHRINE_COMBAT_USE_MESSAGE" )
			chat.paramTokens[0] = Language.GetString("LOGBOOK_CATEGORY_MONSTER").ToLower();
	}

	[HarmonyPatch(typeof(BossGroup), nameof(BossGroup.UpdateBossMemories))]
	[HarmonyPostfix]
	static void UpdateTitle(BossGroup __instance)
	{
		if ( ! boss.Value )
			return;

		var health = new Dictionary<(string, string), float>();
		float maximum = 0;

		for ( int i = 0; i < __instance.bossMemoryCount; ++i )
		{
			CharacterBody body = __instance.bossMemories[i].cachedBody;
			if ( ! body ) continue;

			HealthComponent component = body.healthComponent;
			if ( component?.alive is false ) continue;

			string name = Util.GetBestBodyName(body.gameObject);
			string subtitle = body.GetSubtitle();

			var key = ( name, subtitle );
			if ( ! health.ContainsKey(key) )
				health[key] = 0;

			health[key] += component.combinedHealth + component.missingCombinedHealth * 4;

			if ( health[key] > maximum )
				maximum = health[key];
			else continue;

			if ( string.IsNullOrEmpty(subtitle) )
				subtitle = Language.GetString("NULL_SUBTITLE");

			__instance.bestObservedName = name;
			__instance.bestObservedSubtitle = "<sprite name=\"CloudLeft\" tint=1> " +
					subtitle + " <sprite name=\"CloudRight\" tint=1>";
		}
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.SpendAllCreditsOnMapSpawns))]
	[HarmonyPrefix]
	static void PopulateScene(CombatDirector __instance, ref bool __state)
	{
		__state = __instance.resetMonsterCardIfFailed;
		if ( SceneCatalog.mostRecentSceneDef.stageOrder > Run.stagesPerLoop )
			__instance.resetMonsterCardIfFailed = false;
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.SpendAllCreditsOnMapSpawns))]
	[HarmonyPostfix]
	static void RestoreValue(CombatDirector __instance, bool __state)
	{
		__instance.resetMonsterCardIfFailed = __state;
	}
}
