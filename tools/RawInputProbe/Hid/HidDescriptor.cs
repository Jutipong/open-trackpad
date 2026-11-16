namespace RawInputProbe.Hid;

public enum HidReportKind
{
    Input,
    Output,
    Feature,
}

public sealed class HidField
{
    public required HidReportKind Kind { get; init; }
    public required byte ReportId { get; init; }
    public required int BitOffset { get; init; }
    public required int BitSize { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<uint> Usages { get; init; }
    public int LogicalMinimum { get; init; }
    public int LogicalMaximum { get; init; }
    public bool IsConstant { get; init; }
    public bool IsVariable { get; init; }
    public bool IsRelative { get; init; }

    public uint UsageAt(int element) =>
        Usages.Count == 0 ? 0 : Usages[Math.Min(element, Usages.Count - 1)];

    public static uint Pack(uint page, uint usage) => (page << 16) | (usage & 0xFFFF);
}

public sealed class HidReport
{
    public required HidReportKind Kind { get; init; }
    public required byte ReportId { get; init; }
    public int BitLength { get; set; }
    public List<HidField> Fields { get; } = new();

    public int PayloadByteLength => (BitLength + 7) / 8;
    public bool HasReportId => ReportId != 0;
    public int TotalByteLength => PayloadByteLength + (HasReportId ? 1 : 0);
}

public sealed class HidDescriptor
{
    public bool UsesReportIds { get; private set; }
    public int Length { get; private set; }
    public List<uint> TopLevelCollections { get; } = new();
    public List<HidReport> Reports { get; } = new();

    public IEnumerable<HidReport> InputReports => Reports.Where(r => r.Kind == HidReportKind.Input);
    public IEnumerable<HidReport> OutputReports => Reports.Where(r => r.Kind == HidReportKind.Output);
    public IEnumerable<HidReport> FeatureReports => Reports.Where(r => r.Kind == HidReportKind.Feature);

    public HidReport? GetReport(HidReportKind kind, byte reportId) =>
        Reports.FirstOrDefault(r => r.Kind == kind && r.ReportId == reportId);

    public bool IsPrecisionTouchpad =>
        TopLevelCollections.Any(u => u == HidField.Pack(0x0D, 0x05));

    public static HidDescriptor Parse(ReadOnlySpan<byte> data)
    {
        var descriptor = new HidDescriptor { Length = data.Length };
        var state = new GlobalState();
        var stack = new Stack<GlobalState>();
        var usages = new List<uint>();
        uint? usageMinimum = null;
        uint? usageMaximum = null;
        int collectionDepth = 0;
        var bitOffsets = new Dictionary<(HidReportKind Kind, byte Id), int>();

        int i = 0;
        while (i < data.Length)
        {
            byte prefix = data[i++];
            if (prefix == 0xFE)
            {
                // Long item: 0xFE, data size, tag, data.
                if (i + 2 > data.Length) break;
                int longSize = data[i];
                i += 2;
                i = Math.Min(data.Length, i + longSize);
                continue;
            }

            int size = prefix & 0x03;
            if (size == 3) size = 4;
            int type = (prefix >> 2) & 0x03;
            int tag = (prefix >> 4) & 0x0F;

            if (i + size > data.Length) break;
            uint value = 0;
            for (int b = 0; b < size; b++) value |= (uint)data[i + b] << (8 * b);
            i += size;

            switch (type)
            {
                case 0: // Main
                    switch (tag)
                    {
                        case 0x8:
                        case 0x9:
                        case 0xB:
                            AddReport(descriptor, state, usages, usageMinimum, usageMaximum,
                                tag switch
                                {
                                    0x8 => HidReportKind.Input,
                                    0x9 => HidReportKind.Output,
                                    _ => HidReportKind.Feature,
                                },
                                value, bitOffsets);
                            usages.Clear();
                            usageMinimum = null;
                            usageMaximum = null;
                            break;

                        case 0xA: // Collection
                            if (collectionDepth == 0)
                            {
                                if (usages.Count > 0)
                                    descriptor.TopLevelCollections.Add(usages[0]);
                                else
                                    descriptor.TopLevelCollections.Add(HidField.Pack(state.UsagePage, 0));
                            }
                            collectionDepth++;
                            usages.Clear();
                            usageMinimum = null;
                            usageMaximum = null;
                            break;

                        case 0xC: // End Collection
                            if (collectionDepth > 0) collectionDepth--;
                            usages.Clear();
                            usageMinimum = null;
                            usageMaximum = null;
                            break;
                    }
                    break;

                case 1: // Global
                    switch (tag)
                    {
                        case 0x0: state.UsagePage = value; break;
                        case 0x1: state.LogicalMinimum = unchecked((int)value); break;
                        case 0x2: state.LogicalMaximum = unchecked((int)value); break;
                        case 0x7: state.ReportSize = (int)value; break;
                        case 0x8: state.ReportId = (byte)value; descriptor.UsesReportIds |= value != 0; break;
                        case 0x9: state.ReportCount = (int)value; break;
                        case 0xA: stack.Push(state); break;
                        case 0xB: if (stack.Count > 0) state = stack.Pop(); break;
                    }
                    break;

                case 2: // Local
                    switch (tag)
                    {
                        case 0x0:
                            usages.Add(size == 4 ? value : HidField.Pack(state.UsagePage, value));
                            break;
                        case 0x1:
                            usageMinimum = size == 4 ? value : HidField.Pack(state.UsagePage, value);
                            break;
                        case 0x2:
                            usageMaximum = size == 4 ? value : HidField.Pack(state.UsagePage, value);
                            break;
                    }
                    break;
            }
        }

        return descriptor;
    }

    private static void AddReport(
        HidDescriptor descriptor,
        GlobalState state,
        List<uint> usages,
        uint? usageMinimum,
        uint? usageMaximum,
        HidReportKind kind,
        uint flags,
        Dictionary<(HidReportKind, byte), int> bitOffsets)
    {
        var key = (kind, state.ReportId);
        bitOffsets.TryGetValue(key, out int bitOffset);

        var report = descriptor.GetReport(kind, state.ReportId);
        if (report is null)
        {
            report = new HidReport { Kind = kind, ReportId = state.ReportId };
            descriptor.Reports.Add(report);
        }

        int count = Math.Max(1, state.ReportCount);
        var elementUsages = new List<uint>(count);
        if (usages.Count > 0)
        {
            for (int i = 0; i < count; i++)
                elementUsages.Add(usages[Math.Min(i, usages.Count - 1)]);
        }
        else if (usageMinimum.HasValue && usageMaximum.HasValue)
        {
            uint min = usageMinimum.Value;
            uint max = usageMaximum.Value;
            uint page = min >> 16;
            uint usage = min & 0xFFFF;
            uint maxUsage = max & 0xFFFF;
            for (int i = 0; i < count; i++)
                elementUsages.Add(HidField.Pack(page, usage + (uint)i));
            // Avoid silently generating a huge list if the descriptor is bogus.
            if (elementUsages.Count > 0 && (elementUsages[^1] & 0xFFFF) > maxUsage)
                elementUsages[^1] = max;
        }
        else
        {
            elementUsages.Add(0);
        }

        usages.Clear();

        report.Fields.Add(new HidField
        {
            Kind = kind,
            ReportId = state.ReportId,
            BitOffset = bitOffset,
            BitSize = state.ReportSize,
            Count = count,
            Usages = elementUsages,
            LogicalMinimum = state.LogicalMinimum,
            LogicalMaximum = state.LogicalMaximum,
            IsConstant = (flags & 0x01) != 0,
            IsVariable = (flags & 0x02) != 0,
            IsRelative = (flags & 0x04) != 0,
        });

        int next = bitOffset + state.ReportSize * count;
        bitOffsets[key] = next;
        report.BitLength = Math.Max(report.BitLength, next);
    }

    private struct GlobalState
    {
        public uint UsagePage;
        public int LogicalMinimum;
        public int LogicalMaximum;
        public int ReportSize;
        public int ReportCount;
        public byte ReportId;
    }
}
