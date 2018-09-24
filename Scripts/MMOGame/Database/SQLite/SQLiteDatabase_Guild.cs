﻿using Mono.Data.Sqlite;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MultiplayerARPG.MMO
{
    public partial class SQLiteDatabase
    {
        public override async Task<int> CreateGuild(string guildName, string leaderId, string leaderName)
        {
            int id = 0;
            var reader = await ExecuteReader("INSERT INTO guild (guildName, leaderId, leaderName) VALUES (@guildName, @leaderId, @leaderName);" +
                "SELECT LAST_INSERT_ROWID();",
                new SqliteParameter("@guildName", guildName),
                new SqliteParameter("@leaderId", leaderId),
                new SqliteParameter("@leaderName", leaderName));
            if (reader.Read())
                id = (int)reader.GetInt64(0);
            if (id > 0)
                await ExecuteNonQuery("UPDATE characters SET guildId=@id WHERE id=@leaderId",
                    new SqliteParameter("@id", id),
                    new SqliteParameter("@leaderId", leaderId));
            return id;
        }

        public override async Task<GuildData> ReadGuild(int id)
        {
            GuildData result = null;
            var reader = await ExecuteReader("SELECT * FROM guild WHERE id=@id LIMIT 1",
                new SqliteParameter("@id", id));
            if (reader.Read())
            {
                result = new GuildData(id, reader.GetString("guildName"), reader.GetString("leaderId"), reader.GetString("leaderName"));
                reader = await ExecuteReader("SELECT id, dataId, characterName, level FROM characters WHERE guildId=@id",
                    new SqliteParameter("@id", id));
                SocialCharacterData guildMemberData;
                while (reader.Read())
                {
                    // Get some required data, other data will be set at server side
                    guildMemberData = new SocialCharacterData();
                    guildMemberData.id = reader.GetString("id");
                    guildMemberData.characterName = reader.GetString("characterName");
                    guildMemberData.dataId = reader.GetInt32("dataId");
                    guildMemberData.level = reader.GetInt32("level");
                    result.AddMember(guildMemberData);
                }
            }
            return result;
        }

        public override async Task UpdateGuildMessage(int id, string message)
        {
            await ExecuteNonQuery("UPDATE guild SET message=@message WHERE id=@id",
                new SqliteParameter("@message", message),
                new SqliteParameter("@id", id));
        }

        public override async Task DeleteGuild(int id)
        {
            await ExecuteNonQuery("DELETE FROM guild WHERE id=@id;" +
                "UPDATE characters SET guildId=0 WHERE guildId=@id;",
                new SqliteParameter("@id", id));
        }

        public override async Task SetCharacterGuild(string characterId, int guildId)
        {
            await ExecuteNonQuery("UPDATE characters SET guildId=@guildId WHERE id=@characterId",
                new SqliteParameter("@characterId", characterId),
                new SqliteParameter("@guildId", guildId));
        }
    }
}
