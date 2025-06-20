global using HarmonyLib;
global using RoR2;
global using System.Collections.Generic;
global using System.Reflection;
global using UnityEngine;
global using Console = System.Console;
using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Linq;
using System.Security.Permissions;
using static UnityEngine.AddressableAssets.Addressables;

[assembly: AssemblyVersion(Local.Enemy.Variety.Plugin.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace Local.Enemy.Variety;

[BepInPlugin(identifier, "EnemyVariety", version)]
class Plugin : BaseUnityPlugin
{
	public const string version = "0.3.0", identifier = "local.enemy.variety";

	static ConfigEntry<bool> scene, boss, combat;
	static ConfigEntry<float> horde;

	protected async void Awake()
	{
		const string section = "General";

		boss = Config.Bind(
				section, key: "Apply to Teleporter Boss",
				defaultValue: true, description:
					"If enabled, multiple types of bosses may appear for the teleporter event."
			);

		horde = Config.Bind(
				section, key: "Horde of Many",
				defaultValue: 5f, new ConfigDescription(
					"Percent chance for a different type of monster to be chosen instead.",
					new AcceptableValueRange<float>(0, 100))
			);

		scene = Config.Bind(
				section, key: "Scene Director",
				defaultValue: true, description:
					"This determines if expensive enemies are favored during initialization."
			);

		combat = Config.Bind(
				section, key: "Shrine of Combat",
				defaultValue: true, description:
					"Whether those summoned by this interactable should be affected."
			);

		Harmony.CreateAndPatchAll(typeof(Plugin));
		Harmony.CreateAndPatchAll(typeof(Hook));

		var obj = await LoadAssetAsync<GameObject>("RoR2/DLC2/ShrineHalcyonite.prefab").Task;
		if ( obj ) foreach ( var director in obj.GetComponentsInChildren<CombatDirector>() )
			director.enabled = false;
	}

	[HarmonyPatch(typeof(CombatDirector), nameof(CombatDirector.AttemptSpawnOnTarget))]
	[HarmonyPrefix]
	static void ResetMonsterCard(CombatDirector __instance)
	{
		WeightedSelection<DirectorCard> original = __instance.finalMonsterCardsSelection;
		if ( original is null || __instance.resetMonsterCardIfFailed is false )
			return;

		DirectorCard card = __instance.currentMonsterCard;
		Xoroshiro128Plus generator = __instance.rng;

		bool? invalid = null;
		switch ( Override.Get(__instance) )
		{
			case Type.Scene:
				SceneDef current = SceneCatalog.currentSceneDef;
				if ( scene.Value && current?.stageOrder <= Run.stagesPerLoop )
					break;
				return;

			case Type.Boss:
				if ( boss.Value )
				{
					if ( __instance.hasStartedWave )
					{
						invalid = false;
						if ( card is not null && card.IsBoss() )
							break;
					}
					else
					{
						invalid = generator.nextNormalizedFloat < horde.Value / 100;
						if ( card is null || card.IsBoss() == invalid )
							break;
					}
				}
				return;

			case Type.Shrine:
				if ( combat.Value )
					break;
				return;

			case Type.Card:
				return;
		}

		int count = __instance.spawnCountInCurrentWave, previous = 0;

		if ( card is not null ) previous = card.cost;
		else if ( invalid is null ) return;

		var selection = new WeightedSelection<DirectorCard>(original.Count);
		float credit = __instance.monsterCredit, limit = Math.Min(800, credit);

		for ( int i = 0; i < original.Count; ++i )
		{
			WeightedSelection<DirectorCard>.ChoiceInfo choice = original.GetChoice(i);
			card = choice.value;

			if ( card.cost > previous && card.cost > credit )
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
			card = selection.Evaluate(generator.nextNormalizedFloat);

			__instance.PrepareNewMonsterWave(card);
			__instance.spawnCountInCurrentWave = count;
		}
	}

	[HarmonyPrefix, HarmonyPatch(typeof(Chat),
			nameof(Chat.SendBroadcastChat), [ typeof(ChatMessageBase) ])]
	static void ChangeMessage(ChatMessageBase message)
	{
		if ( message is Chat.SubjectFormatChatMessage chat && chat.paramTokens?.Any() is true
				&& combat.Value && chat.baseToken is "SHRINE_COMBAT_USE_MESSAGE" )
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
	[HarmonyILManipulator]
	static void SkipReroll(ILContext context)
	{
		ILCursor cursor = new(context);
		MethodInfo method = typeof(CombatDirector).GetMethod(
				nameof(CombatDirector.PrepareNewMonsterWave),
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
			);

		if ( cursor.TryGotoNext(MoveType.After, ( Instruction i ) => i.MatchCall(method)) )
		{
			ILLabel label = cursor.MarkLabel();
			if ( cursor.TryGotoPrev(MoveType.After, ( Instruction i ) => i.MatchBr(out _ )) )
			{
				cursor.MoveAfterLabels();

				cursor.EmitDelegate(( ) => scene.Value );
				cursor.Emit(OpCodes.Brtrue, label);

				return;
			}
		}

		Console.WriteLine("Failed to patch scene combat director.");
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
