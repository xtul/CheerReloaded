﻿using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CheerReloaded {
	internal class CheerAIBehaviour : MissionBehaviour {
		public override MissionBehaviourType BehaviourType => MissionBehaviourType.Other;

		private readonly Config _config;
		private readonly Strings _strings;
		private readonly CheerCommonMethods _common;

		public CheerAIBehaviour(Config config, CheerCommonMethods common, Strings strings) {
			_config = config;
			_strings = strings;
			_common = common;
		}

		public override void OnAgentCreated(Agent agent) {
			if (agent == null) return;
			if (agent.IsMount) return;
			if (agent.IsMainAgent) return;
			if (agent.Character == null) return;

			if (agent.IsHero) {
				agent.AddComponent(new CheerAIComponent(_config, agent, _common, _strings));
				if (_config.AI.ImpactfulDeath) {
					agent.OnAgentHealthChanged += OnHitPointsChanged;
				}
			}
 		}

		private void OnHitPointsChanged(Agent agent, float oldHealth, float newHealth) {
			if (newHealth < 1f) {
				var agentsToAffect = Mission.Current.GetAgentsInRange(agent.Position.AsVec2, 75f)
													.Where(x => x != null)
													.Where(x => x.IsMount == false)
													.Where(x => x.Character != null)
													.Where(x => x.Health > 0)
													.Where(x => x.IsMainAgent == false)
													.Where(x => x.IsFriendOf(agent));
				
				foreach (var a in agentsToAffect) {
					var curMorale = a.GetMorale();
					a.SetMorale(curMorale - 5f);
				}

				Helpers.Announce(_strings.Lord.Died
								.Replace("$NAME$", agent.Name)
								.Replace("$HISHERUPPER$", agent.IsFemale ? "Her" : "His")
								.Replace("$HISHERLOWER$", agent.IsFemale ? "her" : "his")
								.Replace("$MORALEHIT$", _config.AI.DeathMoraleDecrease.ToString())

				);
			}
		}
	}
}