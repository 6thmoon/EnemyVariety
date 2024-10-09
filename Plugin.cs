using BepInEx;
using BepInEx.Configuration;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static On.RoR2.CombatDirector;
using static On.RoR2.Chat;
using static On.RoR2.BossGroup;

namespace DirectorRework
{
  [BepInPlugin("com.Nuxlar.DirectorRework", "DirectorRework", "1.1.1")]

  public class DirectorRework : BaseUnityPlugin
  {

    private ConfigEntry<bool> teleporterBoss;

    public void Awake()
    {
      AttemptSpawnOnTarget += ResetMonsterCard;
      SendBroadcastChat_ChatMessageBase += ChangeMessage;
      UpdateBossMemories += UpdateTitle;
      SpendAllCreditsOnMapSpawns += PopulateScene;

      const string description = "If enabled, multiple boss types may appear.";
      teleporterBoss = Config.Bind("General", "Apply to Teleporter Boss", true, description);
    }

    private bool ResetMonsterCard(orig_AttemptSpawnOnTarget orig, CombatDirector self,
        Transform target, DirectorPlacementRule.PlacementMode mode)
    {
      bool result = false;
      ref DirectorCard card = ref self.currentMonsterCard;

      if (card != null && self.resetMonsterCardIfFailed)
      { // Don't apply to the 1st monster in a wave, or unique cases like Void Fields
        int count = self.spawnCountInCurrentWave, previous = card.cost;
        do
        {
          if (self == TeleporterInteraction.instance?.bossDirector)
          {
            if (teleporterBoss.Value)
            {
              self.SetNextSpawnAsBoss();
              result = count is 0 || card.cost <= self.monsterCredit;
            } // Retry if failed due to node placement
            else break;
          }
          else
          {
            Xoroshiro128Plus rng = self.rng;

            do card = self.finalMonsterCardsSelection.Evaluate(rng.nextNormalizedFloat);
            while (card.cost / self.monsterCredit < rng.nextNormalizedFloat);

            self.PrepareNewMonsterWave(card); // Generate a new elite type
          }

        } // Prevent wave from ending early e.g. due to Overloading Worm
        while (card.cost > previous && card.cost > self.monsterCredit);

        self.spawnCountInCurrentWave = count; // Reset to zero; restore previous value
      }

      result |= orig(self, target, mode);
      return result;
    }

    private void ChangeMessage(orig_SendBroadcastChat_ChatMessageBase orig, ChatMessageBase message)
    {
      if (message is Chat.SubjectFormatChatMessage chat && chat.paramTokens?.Any() is true
          && chat.baseToken is "SHRINE_COMBAT_USE_MESSAGE")
        chat.paramTokens[0] = Language.GetString("LOGBOOK_CATEGORY_MONSTER").ToLower();

      // Replace with generic message since shrine will have multiple enemy types
      orig(message);
    }

    private void UpdateTitle(orig_UpdateBossMemories orig, BossGroup self)
    {
      orig(self);
      if (!teleporterBoss.Value) return;

      var health = new Dictionary<(string, string), float>();
      float maximum = 0;

      for (int i = 0; i < self.bossMemoryCount; ++i)
      {
        CharacterBody body = self.bossMemories[i].cachedBody;
        if (!body) continue;

        HealthComponent component = body.healthComponent;
        if (component?.alive is false) continue;

        string name = Util.GetBestBodyName(body.gameObject);
        string subtitle = body.GetSubtitle();

        var key = (name, subtitle);
        if (!health.ContainsKey(key))
          health[key] = 0;

        health[key] += component.combinedHealth + component.missingCombinedHealth * 4;
        // Use title for enemy with the most total health and damage received
        if (health[key] > maximum)
          maximum = health[key];
        else continue;

        if (string.IsNullOrEmpty(subtitle))
          subtitle = Language.GetString("NULL_SUBTITLE");

        self.bestObservedName = name;
        self.bestObservedSubtitle = "<sprite name=\"CloudLeft\" tint=1> " +
            subtitle + " <sprite name=\"CloudRight\" tint=1>";
      }
    }

    private void PopulateScene(
        orig_SpendAllCreditsOnMapSpawns orig, CombatDirector self, Transform target)
    {
      if (SceneCatalog.mostRecentSceneDef.stageOrder > Run.stagesPerLoop)
      {
        bool value = self.resetMonsterCardIfFailed;
        self.resetMonsterCardIfFailed = false;

        orig(self, target);
        self.resetMonsterCardIfFailed = value;
      }
      else orig(self, target);
    }

  }
}
