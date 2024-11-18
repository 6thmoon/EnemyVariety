using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine;
using static UnityEngine.AddressableAssets.Addressables;

[assembly: AssemblyVersion(Local.Enemy.Variety.Plugin.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace Local.Enemy.Variety;

[BepInPlugin(identifier, "EnemyVariety", version)]
class Plugin : BaseUnityPlugin
{
	public const string version = "0.3.0", identifier = "local.enemy.variety";
	static ConfigEntry<bool> boss; static ConfigEntry<float> horde;

	protected async void Awake()
	{
		Harmony.CreateAndPatchAll(typeof(Plugin));

		boss = Config.Bind(
				section: "General", key: "Apply to Teleporter Boss",
				defaultValue: true, description:
					"If enabled, multiple types of bosses may appear for the teleporter event."
			);

		horde = Config.Bind(
				section: "General", key: "Horde of Many",
				defaultValue: 3f, new ConfigDescription(
					"Percent chance for a different type of enemy to be chosen instead.",
					new AcceptableValueRange<float>(0, 100))
			);

		var obj = await LoadAssetAsync<GameObject>("RoR2/DLC2/ShrineHalcyonite.prefab").Task;
		if ( obj ) foreach ( var director in obj.GetComponentsInChildren<CombatDirector>() )
			director.resetMonsterCardIfFailed = false;
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.AttemptSpawnOnTarget))]
	[HarmonyPrefix]
	static void ResetMonsterCard(CombatDirector __instance)
	{
		DirectorCard card = __instance.currentMonsterCard;
		WeightedSelection<DirectorCard> original = __instance.finalMonsterCardsSelection;

		if ( card is null || __instance.resetMonsterCardIfFailed is false || original is null )
			return;

		int count = __instance.spawnCountInCurrentWave, previous = card.cost;
		Xoroshiro128Plus rng = __instance.rng;

		bool? invalid = null;
		if ( __instance == TeleporterInteraction.instance?.bossDirector )
		{
			if ( boss.Value is false )
				return;
			else if ( count is 0 )
			{
				invalid = rng.nextNormalizedFloat < horde.Value / 100;
				previous = 0;
			}
			else if ( card.IsBoss() )
				invalid = false;
			else return;
		}

		var selection = new WeightedSelection<DirectorCard>(original.Count);
		float limit = Math.Min(800, __instance.monsterCredit);

		for ( int i = 0; i < original.Count; ++i )
		{
			WeightedSelection<DirectorCard>.ChoiceInfo choice = original.GetChoice(i);
			card = choice.value;

			if ( card.cost > previous && card.cost > __instance.monsterCredit )
				continue;
			else if ( card.IsAvailable() )
			{
				if ( invalid is null )
					choice.weight *= Math.Min(card.cost, limit);
				else if ( card.IsBoss() == invalid )
					continue;

				selection.AddChoice(choice);
			}
		}

		if ( selection.Count > 0 )
		{
			__instance.PrepareNewMonsterWave(selection.Evaluate(rng.nextNormalizedFloat));
			__instance.spawnCountInCurrentWave = count;
		}
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

static class Extension
{
	internal static bool IsBoss(this DirectorCard card)
	{
		if ( card.spawnCard is not CharacterSpawnCard character || character.forbiddenAsBoss )
			return false;

		GameObject prefab = character.prefab;
		prefab = prefab.GetComponent<CharacterMaster>().bodyPrefab;

		return prefab.GetComponent<CharacterBody>().isChampion;
	}
}
