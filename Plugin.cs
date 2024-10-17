using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.Networking;
using static RoR2.TeleporterInteraction;
using static UnityEngine.AddressableAssets.Addressables;

[assembly: AssemblyVersion(Local.Enemy.Variety.Plugin.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace Local.Enemy.Variety;

[BepInPlugin(identifier, "EnemyVariety", version)]
class Plugin : BaseUnityPlugin
{
	public const string version = "0.2.1", identifier = "local.enemy.variety";
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
				defaultValue: 5f, new ConfigDescription(
					"Percent chance for a different type of enemy to be chosen instead.",
					new AcceptableValueRange<float>(0, 100))
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
		if ( ! __instance.resetMonsterCardIfFailed || card == null || selection == null )
			return;

		foreach ( WeightedSelection<DirectorCard>.ChoiceInfo choice in selection.choices )
		{
			if ( ! object.Equals(card, choice.value) || choice.weight <= 0 || card.cost <= 0 )
				continue;

			int count = __instance.spawnCountInCurrentWave, previous = card.cost;
			Xoroshiro128Plus rng = __instance.rng;

			do
			{
				if ( __instance == instance?.bossDirector )
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
					float threshold = Mathf.Min(800, __instance.monsterCredit);

					do card = selection.Evaluate(rng.nextNormalizedFloat);
					while ( card.cost < rng.nextNormalizedFloat * threshold );

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

	[HarmonyPatch(typeof(ChargingState), nameof(ChargingState.OnEnter))]
	[HarmonyPostfix]
	static void SummonTheHorde(ChargingState __instance)
	{
		if ( ! NetworkServer.active )
			return;

		CombatDirector instance = __instance.bossDirector;
		if ( ! instance ) return;

		WeightedSelection<DirectorCard> original = instance.finalMonsterCardsSelection;
		if ( original == null ) return;

		Xoroshiro128Plus rng = instance.rng;
		if ( rng.nextNormalizedFloat >= horde.Value / 100 ) return;

		var selection = new WeightedSelection<DirectorCard>();
		for ( int i = 0; i < original.Count; ++i )
		{
			WeightedSelection<DirectorCard>.ChoiceInfo choice = original.GetChoice(i);
			DirectorCard card = choice.value;

			if ( card.IsAvailable() )
			{
				GameObject prefab = card.spawnCard.prefab;
				prefab = prefab.GetComponent<CharacterMaster>().bodyPrefab;

				if ( prefab.GetComponent<CharacterBody>().isChampion )
					continue;

				selection.AddChoice(choice);
			}
		}

		if ( selection.totalWeight > 0 && selection.Count > 0 )
		{
			instance.currentMonsterCard = null;
			instance.monsterCardsSelection = selection;
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
