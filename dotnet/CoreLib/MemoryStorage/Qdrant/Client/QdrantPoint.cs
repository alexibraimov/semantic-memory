﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.AI.Embeddings;

namespace Microsoft.SemanticMemory.Core.MemoryStorage.Qdrant.Client;

/// <summary>
/// A record structure used by Qdrant that contains an embedding and metadata.
/// </summary>
internal class QdrantPoint<T> where T : DefaultQdrantPayload, new()
{
    [JsonPropertyName(QdrantConstants.PointIdField)]
    public Guid Id { get; set; } = Guid.Empty;

    [JsonPropertyName(QdrantConstants.PointVectorField)]
    [JsonConverter(typeof(ReadOnlyMemoryConverter))]
    public ReadOnlyMemory<float> Vector { get; set; } = new();

    [JsonPropertyName(QdrantConstants.PointPayloadField)]
    public T Payload { get; set; } = new();

    public MemoryRecord ToMemoryRecord(bool withEmbedding = true)
    {
        MemoryRecord result = new()
        {
            Id = this.Payload.Id,
            Payload = JsonSerializer.Deserialize<Dictionary<string, object>>(this.Payload.Payload, QdrantConfig.JSONOptions)
                      ?? new Dictionary<string, object>()
        };

        if (withEmbedding)
        {
            result.Vector = new Embedding<float>(this.Vector.ToArray());
        }

        foreach (string[] keyValue in this.Payload.Tags.Select(tag => tag.Split('=', 2)))
        {
            string key = keyValue[0];
            string? value = keyValue.Length == 1 ? null : keyValue[1];
            result.Tags.Add(key, value);
        }

        return result;
    }

    public static QdrantPoint<T> FromMemoryRecord(MemoryRecord record)
    {
        return new QdrantPoint<T>
        {
            Vector = record.Vector.Vector.ToArray(),
            Payload = new T
            {
                Id = record.Id,
                Tags = record.Tags.Pairs.Select(tag => $"{tag.Key}={tag.Value}").ToList(),
                Payload = JsonSerializer.Serialize(record.Payload, QdrantConfig.JSONOptions),
            }
        };
    }
}
