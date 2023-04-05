﻿using LiteNetLib.Utils;

namespace MultiplayerARPG.MMO
{
    public partial struct GetIdByCharacterNameResp : INetSerializable
    {
        public string Id { get; set; }

        public void Deserialize(NetDataReader reader)
        {
            Id = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Id);
        }
    }
}