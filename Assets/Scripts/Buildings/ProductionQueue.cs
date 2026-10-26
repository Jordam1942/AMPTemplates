using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vaste FIFO-wachtrij voor productie. Alleen het voorste item loopt; de rest wacht.
/// Geen allocaties tijdens het spelen: de lijst wordt eenmaal met de capaciteit aangemaakt en Entry is een struct.
/// </summary>
public sealed class ProductionQueue
{
    private struct Entry
    {
        public ProductionOption Option;
        public float Total;
        public float Remaining;
    }

    private readonly List<Entry> entries;

    public int Capacity { get; }
    public int Count => entries.Count;
    public bool IsFull => entries.Count >= Capacity;

    /// <summary>Voortgang van het voorste item, van 0 tot 1.</summary>
    public float FrontProgress => entries.Count == 0 ? 0f : 1f - entries[0].Remaining / entries[0].Total;

    public ProductionQueue(int capacity)
    {
        Capacity = Mathf.Max(1, capacity);
        entries = new List<Entry>(Capacity);
    }

    /// <summary>Voegt een optie toe. Geeft false terug als de wachtrij vol is.</summary>
    public bool TryEnqueue(ProductionOption option)
    {
        if (IsFull) return false;

        float total = Mathf.Max(0.01f, option.time);
        entries.Add(new Entry { Option = option, Total = total, Remaining = total });
        return true;
    }

    /// <summary>
    /// Tikt het voorste item. Geeft true terug, met de voltooide optie, wanneer het klaar is.
    /// Maximaal één voltooiing per tik. Resttijd van een groot frame gaat niet verloren, want het item blijft staan.
    /// </summary>
    public bool Tick(float deltaTime, float speedMultiplier, out ProductionOption completed)
    {
        completed = default;
        if (entries.Count == 0) return false;

        Entry front = entries[0];
        front.Remaining -= deltaTime * speedMultiplier;

        if (front.Remaining > 0f)
        {
            entries[0] = front;
            return false;
        }

        completed = front.Option;
        entries.RemoveAt(0);
        return true;
    }

    /// <summary>Leest item 'index' zonder de lijst te kopiëren. Index 0 is het voorste item, het enige dat loopt.</summary>
    public bool TryGetEntry(int index, out ProductionOption option, out float progress)
    {
        option = default;
        progress = 0f;
        if (index < 0 || index >= entries.Count) return false;

        Entry entry = entries[index];
        option = entry.Option;
        progress = index == 0 ? 1f - entry.Remaining / entry.Total : 0f;
        return true;
    }

    /// <summary>Annuleert het laatst toegevoegde item, zodat de kosten terugbetaald kunnen worden.</summary>
    public bool TryCancelLast(out ProductionOption cancelled)
    {
        cancelled = default;
        if (entries.Count == 0) return false;

        int last = entries.Count - 1;
        cancelled = entries[last].Option;
        entries.RemoveAt(last);
        return true;
    }
}
