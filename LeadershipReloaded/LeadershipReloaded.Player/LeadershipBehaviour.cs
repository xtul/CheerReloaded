﻿using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using LeadershipReloaded.Common;
using LeadershipReloaded.Settings;

namespace LeadershipReloaded.Player {
	internal class LeadershipBehaviour : MissionBehaviour {
		public override MissionBehaviourType BehaviourType => MissionBehaviourType.Other;

		private readonly Config _config;
		private readonly Strings _strings;
		private readonly CheerCommonMethods _common;
		private readonly SoundManager _soundManager;
		private readonly Random _rng;
		private float _effectRadius;
		private int _moraleChange;
		private int _cheerAmount;
		private bool _canCheer;

		public LeadershipBehaviour(Config config, CheerCommonMethods common, Strings strings) {
			_config = config;
			_strings = strings;
			_canCheer = true;
			_common = common;
			_rng = new Random();
			_soundManager = new SoundManager(config);
		}

		public override void OnAgentBuild(Agent agent, Banner banner) {
			if (agent == null) return;
			if (banner == null) return;

			if (agent == Agent.Main) {
				_cheerAmount = _config.Cheering.BaselineCheerAmount;
				var leadership = agent.Character?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
				_cheerAmount += Math.DivRem(leadership, _config.Cheering.CheersPerXLeadershipLevels, out _);
				Helpers.Say("{=cheercounter}" + _strings.CheerCounter.Replace("$COUNT$", _cheerAmount.ToString()),
							new Dictionary<string, TextObject> {
								{ "COUNT", new TextObject(_cheerAmount.ToString()) }
							}
				);
			}
		}

		/// <summary>
		/// Runs on every tick of the game. Considering it runs extremely often only the lightest things should
		/// appear here, such as user input handling.
		/// </summary>
		public override async void OnMissionTick(float dt) {
			if (Agent.Main != null) {
				if (Input.IsKeyReleased((InputKey)_config.Cheering.KeyCode) && _canCheer) {
					if (Mission.Current.Mode != MissionMode.Battle) return;

					_canCheer = false;

					if (!Mission.Current.MissionEnded()) {
						await DoInCombatCheer();
						await Task.Delay(TimeSpan.FromSeconds(3));
					} else {
						Helpers.RunAsync(
							_soundManager.PlayHorn(
								"default",
								Agent.Main.Team.TeamAgents.GetRandomElement(),
								OrderType.GuardMe,
								true
							)
						);
						await DoVictoryCheer();
						await Task.Delay(TimeSpan.FromSeconds(1));
					}

					_canCheer = true;
				}
			}
		}

		/// <summary>
		/// Calculates effect radius and a list of agents to be affected. Ignores cheer limit.
		/// </summary>
		private async Task DoVictoryCheer() {
			try {
				var leadership = Agent.Main.Character?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
				_effectRadius = (leadership * 1.5f).Clamp(100, 700);

				var agentList = Mission.GetAgentsInRange(Agent.Main.Position.AsVec2, _effectRadius)
									.Where(x => x.IsMount == false)
									.Where(x => x.Health > 0)
									.Where(x => x.Character != null)
									.Where(x => x.IsMainAgent == false)
									.Where(x => x.Team.IsFriendOf(Agent.Main.Team))
									.ToList();

				_common.ApplyCheerEffects(Agent.Main, _moraleChange);
				await Task.Delay(TimeSpan.FromSeconds(0.65));

				foreach (var a in agentList) {
					_common.ApplyCheerEffects(a, _moraleChange);
					await Task.Delay(MBRandom.RandomInt(0, 9));
				}
			} catch (Exception ex) {
				if (_config.Cheering.DebugMode) {
					Helpers.Log(ex.Message);
					Clipboard.SetText(ex.Message + "\n" + ex.StackTrace);
				}
			}
		}

		/// <summary>
		/// Calculates morale changed, effect radius, a list of agents to be affected. Handles morale report and applies XP increase.
		/// </summary>
		private async Task DoInCombatCheer() {
			if (_cheerAmount < 1) {
				Helpers.Say("{=failed}" + _strings.Failed);
				return;
			}
			if (Agent.Main == null) return;
			if (Agent.Main.Team == null) return;

			var leadership = Agent.Main.Character?.GetSkillValue(DefaultSkills.Leadership) ?? 0;

			var playerPower = 0f;
			var enemyPower = 0f;
			var mCap = _config.Cheering.MaximumMoralePerAgent;
			var aCap = _config.Cheering.MaximumAdvantageMorale;

			try {
				foreach (var team in Mission.Current.Teams) {
					foreach (var f in team.Formations) {
						if (f.Team.Side == Agent.Main.Team.Side) {
							playerPower += f.GetFormationPower();						
						} else {
							enemyPower += f.GetFormationPower();
						}
					}
				}
			} catch { }

			float advantageBonus = ((playerPower - enemyPower) / 20).Clamp(aCap * -1, aCap);

			_moraleChange = (int)Math.Round(((leadership / 18) + advantageBonus).Clamp(mCap * -1, mCap));
			_effectRadius = (leadership / 2).Clamp(25, 200);

			try {
				var agentsList = Mission.GetAgentsInRange(Agent.Main.Position.AsVec2, _effectRadius)
									.Where(x => x != null)
									.Where(x => x.IsMount == false)
									.Where(x => x.Character != null)
									.Where(x => x.Health > 0)
									.Where(x => x.IsMainAgent == false);

				var friendlyAgentsList = agentsList.Where(x => x != null && x.IsFriendOf(Agent.Main)).ToList();
				var enemyAgentsList = agentsList.Where(x => x != null && x.IsEnemyOf(Agent.Main)).ToList();

				var totalFriendlyMoraleApplied = 0;
				var totalEnemyMoraleApplied = 0;
				_common.ApplyCheerEffects(Agent.Main, _moraleChange);
				await Task.Delay(TimeSpan.FromSeconds(0.65));

				foreach (var a in friendlyAgentsList) {
					_common.ApplyCheerEffects(a, _moraleChange);
					await Task.Delay(_rng.Next(0, 9));
					totalFriendlyMoraleApplied += _common.ApplyMoraleChange(a, _moraleChange, noNegativeMorale: _config.Cheering.PreventNegativeMorale);
				}

				if (leadership >= _config.Cheering.EnemyMoraleLeadershipThreshold) {
					foreach (var a in enemyAgentsList) {
						totalEnemyMoraleApplied += _common.ApplyMoraleChange(a, _moraleChange, true);
						_common.ApplyCheerEffects(a, _moraleChange, false);
						a.MakeVoice(SkinVoiceManager.VoiceType.Victory, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
					}
				}


				if (!(Campaign.Current is null)) {
					var calcResult = CalculateXpDividerAndMaxXP(leadership);					

					var xpToGrant = ((totalFriendlyMoraleApplied + (totalEnemyMoraleApplied * -1)) / calcResult[0])
									.Clamp(0, calcResult[1] * 400);

					var mainHero = Hero.All.Where(x => x.StringId == Agent.Main.Character.StringId).FirstOrDefault();
					// always grant some xp
					if (xpToGrant == 0) xpToGrant = 1;
					mainHero.AddSkillXp(DefaultSkills.Leadership, xpToGrant);
				}

				_cheerAmount--;

				if (_config.Cheering.ReportMoraleChange) {
					if (totalFriendlyMoraleApplied > 0) {
						Helpers.Say("{=friendly_positivemorale}" + _strings.Friendly.PositiveMorale
								.Replace("$AGENTMORALE$", _moraleChange.ToString())
								.Replace("$TOTALMORALE$", totalFriendlyMoraleApplied.ToString()),
								new Dictionary<string, TextObject> {
									{ "AGENTMORALE", new TextObject(_moraleChange) },
									{ "TOTALMORALE", new TextObject(totalFriendlyMoraleApplied) }
								}
						);
						if (leadership >= _config.Cheering.EnemyMoraleLeadershipThreshold && totalEnemyMoraleApplied > 0) {
							Helpers.Say("{=enemy_negativemorale}" + _strings.Enemy.NegativeMorale
									.Replace("$AGENTMORALE$", (_moraleChange / 2).ToString())
									.Replace("$TOTALMORALE$", totalEnemyMoraleApplied.ToString()),
									new Dictionary<string, TextObject> {
										{ "AGENTMORALE", new TextObject(_moraleChange / 2) },
										{ "TOTALMORALE", new TextObject(totalEnemyMoraleApplied) }
									}
							);
						}
					} else if (totalFriendlyMoraleApplied < 0) {
						Helpers.Say("{=friendly_negativemorale}" + _strings.Friendly.NegativeMorale
								.Replace("$AGENTMORALE$", _moraleChange.ToString())
								.Replace("$TOTALMORALE$", totalFriendlyMoraleApplied.ToString()),
								new Dictionary<string, TextObject> {
									{ "AGENTMORALE", new TextObject(_moraleChange) },
									{ "TOTALMORALE", new TextObject(totalFriendlyMoraleApplied) }
								}
						);
						if (leadership >= _config.Cheering.EnemyMoraleLeadershipThreshold && totalEnemyMoraleApplied > 0) {
							Helpers.Say("{=friendly_positivemorale}" + _strings.Enemy.PositiveMorale
									.Replace("$TOTALMORALE$", totalEnemyMoraleApplied.ToString()),
									new Dictionary<string, TextObject> {
										{ "TOTALMORALE", new TextObject(totalEnemyMoraleApplied) }
									}
							);
						}
					} else if (totalEnemyMoraleApplied > 0) {
						Helpers.Say("{=enemymoraleeffect}" + _strings.EnemyMoraleEffect
								.Replace("$AGENTMORALE$", (_moraleChange / 2).ToString())
								.Replace("$TOTALMORALE$", totalEnemyMoraleApplied.ToString()),
								new Dictionary<string, TextObject> {
									{ "AGENTMORALE", new TextObject(_moraleChange / 2) },
									{ "TOTALMORALE", new TextObject(totalEnemyMoraleApplied) }
								}
						);
					} else {
						Helpers.Say("{=noeffect}" + _strings.NoEffect);
						_cheerAmount++;
					}
				} else {
					if (totalFriendlyMoraleApplied == 0 && totalEnemyMoraleApplied == 0) {
						_cheerAmount++;
					}
				}

			} catch (Exception ex) {
				if (_config.Cheering.DebugMode) {
					Helpers.Log(ex.Message);
					Clipboard.SetText(ex.Message + "\n" + ex.StackTrace);
				}
			}

		}

		/// <summary>
		/// Calculates experience divider and maximum xp multiplier.
		/// </summary>
		/// <returns>An array of int, with 0 index as divider and 1 index as multiplier. Zero values on invaild leadership.</returns>
		private int[] CalculateXpDividerAndMaxXP(int leadership) {
			if (leadership.IsInRange(0, 15)) {
				return new int[] { 6, 1 };
			} 
			if (leadership.IsInRange(16, 40)) {
				return new int[] { 5, 2 };
			}
			if (leadership.IsInRange(41, 75)) {
				return new int[] { 4, 3 };
			}
			if (leadership.IsInRange(76, 125)) {
				return new int[] { 3, 4 };
			}
			if (leadership.IsInRange(126, 200)) {
				return new int[] { 2, 5 };
			}
			if (leadership.IsInRange(201)) {
				return new int[] { 1, 6 };
			}
			return new int[] { 0, 0 };
		}
	}
}