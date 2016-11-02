using System;
using Server.Items;

namespace Server.Items
{
	public class VirtuososCollar : LeatherGorget
	{
		public override bool IsArtifact { get { return true; } }
		public override int LabelNumber{ get{ return 1151555; } } // Virtuoso's Collar

        public override SetItem SetID{ get{ return SetItem.Virtuoso; } }
		public override int Pieces{ get{ return 4; } }
	
		public override int BasePhysicalResistance{ get{ return 4; } }
		public override int BaseFireResistance{ get{ return 19; } }
		public override int BaseColdResistance{ get{ return 5; } }
		public override int BasePoisonResistance{ get{ return 5; } }
		public override int BaseEnergyResistance{ get{ return 5; } }
		public override int InitMinHits{ get{ return 125; } }
		public override int InitMaxHits{ get{ return 125; } }

		[Constructable]
		public VirtuososCollar() : base()
		{
            this.Hue = 1374;
            this.Weight = 1;
            this.StrRequirement = 20;
            this.SetHue = 1374;
        }

		public VirtuososCollar( Serial serial ) : base( serial )
		{
		}
		
		public override void Serialize( GenericWriter writer )
		{
			base.Serialize( writer );			
			writer.Write( (int) 0 ); // version
		}
		
		public override void Deserialize(GenericReader reader)
		{
			base.Deserialize( reader );			
			int version = reader.ReadInt();
		}
	}
}