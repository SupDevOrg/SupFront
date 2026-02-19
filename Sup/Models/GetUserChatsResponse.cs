using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sup.Models
{
    public class GetUserChatsResponse
    {
        [JsonPropertyName("user_id")]
        public uint UserId { get; set; }

        [JsonPropertyName("chats")]
        [JsonConverter(typeof(ChatListConverter))]
        public List<ChatInfoDto> Chats { get; set; } = new();
    }

    // Пользовательский конвертер для обработки как списков ID чатов, так и полных объектов чатов
    public class ChatListConverter : JsonConverter<List<ChatInfoDto>>
    {
        public override List<ChatInfoDto> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new List<ChatInfoDto>();

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected array");

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                if (reader.TokenType == JsonTokenType.Number)
                {
                    // Если это просто число, создаём ChatInfoDto с ID
                    var id = reader.GetUInt32();
                    result.Add(new ChatInfoDto { Id = id });
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    // Если это объект, десериализуем его полностью
                    using (var doc = JsonDocument.ParseValue(ref reader))
                    {
                        var chatInfo = new ChatInfoDto();
                        var root = doc.RootElement;

                        if (root.TryGetProperty("id", out var idElem))
                            chatInfo.Id = idElem.GetUInt32();
                        if (root.TryGetProperty("name", out var nameElem))
                            chatInfo.Name = nameElem.GetString() ?? "";
                        if (root.TryGetProperty("last_message", out var msgElem))
                            chatInfo.LastMessage = msgElem.GetString() ?? "";
                        if (root.TryGetProperty("last_message_time", out var timeElem))
                        {
                            if (DateTime.TryParse(timeElem.GetString(), out var parsedTime))
                                chatInfo.LastMessageTime = parsedTime;
                        }

                        result.Add(chatInfo);
                    }
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, List<ChatInfoDto> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}