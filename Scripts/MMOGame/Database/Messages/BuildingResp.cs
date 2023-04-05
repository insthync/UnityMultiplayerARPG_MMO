﻿using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public partial struct BuildingResp : INetSerializable
    {
        public BuildingSaveData BuildingData { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            BuildingData = reader.Get(() => new BuildingSaveData());
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(BuildingData);
        }
    }
}