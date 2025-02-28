using Server.Engines.PartySystem;
using Server.Items;
using Server.Mobiles;
using Server.Multis;
using System;
using System.Collections.Generic;

namespace Server.Engines.Quests
{
    public class ProfessionalBountyQuest : BaseQuest
    {
        public override object Title => 1116708;
        public override object Description => 1116709;
        public override object Refuse => 1116713;
        public override object Uncomplete => 1116714;
        public override object Complete => 1116715;

        private BaseGalleon m_Galleon;
        private BindingPole m_Pole;
        private BindingRope m_Rope;
        private Mobile m_Captain;
        private readonly List<Mobile> m_Helpers = new List<Mobile>();

        public BaseGalleon Galleon => m_Galleon;
        public BindingPole Pole => m_Pole;
        public BindingRope Rope => m_Rope;
        public Mobile Captain { get => m_Captain; set => m_Captain = value; }

        public ProfessionalBountyQuest()
        {
            AddObjective(new BountyQuestObjective());
        }

        public ProfessionalBountyQuest(BaseGalleon galleon)
        {
            m_Galleon = galleon;

            AddObjective(new BountyQuestObjective());
            AddReward(new BaseReward(1116712)); //The gold listed on the bulletin board and a special reward from the officer if captured alive.
        }

        public override void OnAccept()
        {
            base.OnAccept();

            AddPole();

            if (Owner != null)
            {
                m_Rope = new BindingRope(this);
                Owner.AddToBackpack(m_Rope);
            }
        }

        public void OnBound(BaseCreature captain)
        {
            if (Owner != null)
            {
                m_Captain = captain;
                CompileHelpersList(captain);

                for (var index = 0; index < Objectives.Count; index++)
                {
                    BaseObjective obj = Objectives[index];

                    if (obj is BountyQuestObjective objective)
                    {
                        objective.Captured = true;
                        objective.CapturedCaptain = captain;
                    }
                }
            }
        }

        public void OnPirateDeath(BaseCreature captain)
        {
            m_Captain = captain;
            CompileHelpersList(captain);

            for (var index = 0; index < Objectives.Count; index++)
            {
                BaseObjective obj = Objectives[index];

                if (obj is BountyQuestObjective objective)
                {
                    objective.Captured = false;
                    objective.CapturedCaptain = null;
                }
            }
        }

        private void CompileHelpersList(BaseCreature pirate)
        {
            if (Owner == null)
                return;

            Party p = Party.Get(Owner);
            List<DamageStore> rights = pirate.GetLootingRights();

            IPooledEnumerable eable = pirate.GetMobilesInRange(19);
            foreach (Mobile mob in eable)
            {
                if (mob == Owner || !(mob is PlayerMobile))
                    continue;

                Party mobParty = Party.Get(mob);

                //Add party memebers regardless of looting rights
                if (p != null && mobParty != null && p == mobParty)
                {
                    m_Helpers.Add(mob);
                    continue;
                }

                // add those with looting rights
                for (int i = rights.Count - 1; i >= 0; --i)
                {
                    DamageStore ds = rights[i];

                    if (ds.m_HasRight && ds.m_Mobile == mob)
                    {
                        m_Helpers.Add(ds.m_Mobile);
                        break;
                    }
                }
            }
            eable.Free();
        }

        public void AddPole()
        {
            if (m_Galleon == null)
                return;

            int dist = m_Galleon.CaptiveOffset;
            int xOffset = 0;
            int yOffset = 0;
            m_Pole = new BindingPole(this);

            switch (m_Galleon.Facing)
            {
                case Direction.North:
                    xOffset = 0;
                    yOffset = dist * -1;
                    break;
                case Direction.South:
                    xOffset = 0;
                    yOffset = dist * 1;
                    break;
                case Direction.East:
                    yOffset = 0;
                    xOffset = dist * 1;
                    break;
                case Direction.West:
                    xOffset = dist * -1;
                    yOffset = 0;
                    break;
            }

            m_Pole.MoveToWorld(new Point3D(m_Galleon.X + xOffset, m_Galleon.Y + yOffset, m_Galleon.ZSurface), m_Galleon.Map);
            m_Galleon.AddFixture(m_Pole);
        }

        public override void GiveRewards()
        {
            bool captured = false;

            for (var index = 0; index < Objectives.Count; index++)
            {
                BaseObjective obj = Objectives[index];

                if (obj is BountyQuestObjective o && o.Captured)
                {
                    captured = true;

                    if (o.CapturedCaptain is PirateCaptain p)
                    {
                        p.Quest = null;
                    }

                    o.CapturedCaptain = null;
                    o.Captured = false;
                    break;
                }
            }

            if (Owner == null)
                return;

            m_Helpers.Add(Owner);
            int totalAward = 7523;

            if (m_Captain != null && BountyQuestSpawner.Bounties.ContainsKey(m_Captain))
                totalAward = BountyQuestSpawner.Bounties[m_Captain];

            int eachAward = totalAward;

            if (m_Helpers.Count > 1)
                eachAward = totalAward / m_Helpers.Count;

            for (var index = 0; index < m_Helpers.Count; index++)
            {
                Mobile mob = m_Helpers[index];

                if (mob.NetState != null || mob == Owner)
                {
                    mob.AddToBackpack(new Gold(eachAward));

                    if (captured)
                    {
                        Item reward = Loot.Construct(m_CapturedRewards[Utility.Random(m_CapturedRewards.Length)]);

                        if (reward != null)
                        {
                            if (reward is RuinedShipPlans)
                            {
                                mob.SendLocalizedMessage(1149838); //Here is something special!  It's a salvaged set of orc ship plans.  Parts of it are unreadable, but if you could get another copy you might be able to fill in some of the missing parts...
                            }
                            else
                            {
                                mob.SendLocalizedMessage(1149840); //Here is some special cannon ammunition.  It's imported!
                            }

                            if (reward is FlameCannonball || reward is FrostCannonball)
                            {
                                reward.Amount = Utility.RandomMinMax(5, 10);
                            }

                            mob.AddToBackpack(reward);
                        }
                    }

                    mob.SendLocalizedMessage(1149825, string.Format("{0}\t{1}", totalAward, eachAward)); //Here's your share of the ~1_val~ reward money, you get ~2_val~ gold.  You've earned it!
                }
                else
                {
                    for (var i = 0; i < m_Helpers.Count; i++)
                    {
                        Mobile mobile = m_Helpers[i];

                        if (mobile != mob && mobile.NetState != null)
                        {
                            mobile.SendLocalizedMessage(1149837, string.Format("{0}\t{1}\t{2}", eachAward, mob.Name, Owner.Name)); //~1_val~ gold is for ~2_val~, I can't find them so I'm giving this to Captain ~3_val~.
                        }
                    }

                    Owner.AddToBackpack(new Gold(eachAward));
                }
            }

            if (m_Captain != null && m_Captain.Alive)
                m_Captain.Delete();

            base.GiveRewards();
        }

        public override void RemoveQuest(bool removeChain)
        {
            base.RemoveQuest(removeChain);

            if (m_Rope != null && !m_Rope.Deleted)
            {
                m_Rope.Quest = null;
                m_Rope.Delete();
            }

            if (m_Pole != null && !m_Pole.Deleted)
            {
                m_Pole.Quest = null;
                m_Pole.Delete();
            }

            if (m_Galleon != null)
                m_Galleon.CapturedCaptain = null;
        }

        public override bool RenderObjective(MondainQuestGump g, bool offer)
        {
            g.AddHtmlLocalized(98, 172, 312, 32, 1116710, 0x15F90, false, false); // Capture or kill a pirate listed on the bulletin board.

            g.AddHtmlLocalized(98, 220, 312, 32, 1116711, 0x15F90, false, false); // Return to the officer with the pirate or a death certificate for your reward.

            return true;
        }

        private readonly Type[] m_CapturedRewards =
        {
           typeof(RuinedShipPlans), typeof(RuinedShipPlans),
           typeof(FlameCannonball), typeof(FlameCannonball),
           typeof(FrostCannonball), typeof(FrostCannonball),
           typeof(FlameCannonball), typeof(FlameCannonball),
           typeof(FrostCannonball), typeof(FrostCannonball)
        };

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);
            writer.WriteEncodedInt(0); // version

            writer.Write(m_Pole);
            writer.Write(m_Rope);
            writer.Write(m_Captain);
            writer.Write(m_Galleon);

            writer.Write(m_Helpers.Count);
            for (var index = 0; index < m_Helpers.Count; index++)
            {
                Mobile mob = m_Helpers[index];
                writer.Write(mob);
            }
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);
            reader.ReadEncodedInt();

            m_Pole = reader.ReadItem() as BindingPole;
            m_Rope = reader.ReadItem() as BindingRope;
            m_Captain = reader.ReadMobile();
            m_Galleon = reader.ReadItem() as BaseGalleon;

            int count = reader.ReadInt();
            for (int i = 0; i < count; i++)
            {
                Mobile mob = reader.ReadMobile();
                if (mob != null)
                    m_Helpers.Add(mob);
            }

            if (m_Rope != null)
                m_Rope.Quest = this;

            if (m_Pole != null)
                m_Pole.Quest = this;
            else
                AddPole();

            AddReward(new BaseReward(1116712)); //The gold listed on the bulletin board and a special reward from the officer if captured alive.
        }

        public bool HasQuest(PlayerMobile pm)
        {
            if (pm.Quests == null)
                return false;

            for (int i = 0; i < pm.Quests.Count; i++)
            {
                BaseQuest quest = pm.Quests[i];

                if (quest.Quester == this)
                {
                    for (int j = 0; j < quest.Objectives.Count; j++)
                    {
                        if (quest.Objectives[j].Update(pm))
                            quest.Objectives[j].Complete();
                    }

                    if (quest.Completed)
                    {
                        quest.OnCompleted();
                        pm.SendGump(new MondainQuestGump(quest, MondainQuestGump.Section.Complete, false, true));
                    }
                    else
                    {
                        pm.SendGump(new MondainQuestGump(quest, MondainQuestGump.Section.InProgress, false));
                        quest.InProgress();
                    }

                    return true;
                }
            }
            return false;

        }
    }
}
