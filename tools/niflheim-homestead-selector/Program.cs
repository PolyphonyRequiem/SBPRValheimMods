using System.Globalization;
using System.Text.Json;
using SBPR.Niflheim.HomesteadStones.Domain;

if (args.Length != 4 || args[0] != "generate")
{
    Console.Error.WriteLine("usage: generate <astley-real-locations.tsv> <output.json> <world-uid>");
    return 2;
}

var source = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var worldUid = long.Parse(args[3], CultureInfo.InvariantCulture);
const string selectorVersion = "niflheim-homestead-playtest-v1";
const double minimumDistance = 128.0;
const double density = 0.40;
var eligible = Enumerable.Range(1, 13).Select(i => "WoodHouse" + i)
    .Concat(new[] { "WoodFarm1", "WoodVillage1" }).ToHashSet(StringComparer.Ordinal);

var candidates = new List<HomesteadCandidate>();
foreach (var raw in File.ReadLines(source))
{
    if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#", StringComparison.Ordinal)) continue;
    var p = raw.Split('|');
    if (p.Length != 8 || !eligible.Contains(p[0])) continue;
    candidates.Add(new HomesteadCandidate(
        p[0],
        int.Parse(p[1], CultureInfo.InvariantCulture),
        int.Parse(p[2], CultureInfo.InvariantCulture),
        double.Parse(p[3], CultureInfo.InvariantCulture),
        double.Parse(p[5], CultureInfo.InvariantCulture),
        double.Parse(p[6], CultureInfo.InvariantCulture)));
}

var worldIdentity = HomesteadWorldIdentity.FromUid(worldUid);
var config = new HomesteadSelectionConfig(worldIdentity, selectorVersion, minimumDistance, density);
var selection = HomesteadSelector.Select(candidates, config);
var selected = selection.Selected.Select(candidate => new
{
    prefab = candidate.Prefab,
    zone = new[] { candidate.ZoneX, candidate.ZoneZ },
    position = new[] { candidate.X, 0.0, candidate.Z },
    area = candidate.LocationRadius,
    prioritySha256 = Convert.ToHexString(HomesteadSelector.Priority(config, candidate)),
    seatAttempts = HomesteadSeatGenerator.Generate(worldIdentity, selectorVersion, candidate, 8)
        .Select(seat => new { index = seat.Attempt, rawXZ = new[] { seat.X, seat.Z } })
        .ToArray(),
}).ToArray();
var targets = candidates.GroupBy(c => c.Prefab, StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => (int)Math.Ceiling(g.Count() * density), StringComparer.Ordinal);
var assigned = selection.Selected.GroupBy(c => c.Prefab, StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
var pairwiseMinimum = selected.Length < 2 ? 0.0 : selection.Selected
    .SelectMany((a, i) => selection.Selected.Skip(i + 1).Select(b => Math.Sqrt(a.DistanceSquaredTo(b))))
    .Min();
var document = new
{
    schema = "niflheim-homestead-selector-v1",
    world = new { uid = worldUid, identity = worldIdentity },
    selector = new
    {
        version = selectorVersion,
        minimumDistance,
        density,
        priorityShape = "world UID + selector version + prefab key + location zone coord",
        seatShape = "world UID + selector version + seat + prefab key + location zone coord + attempt",
        algorithm = "stable SHA-256 type-local priority; deterministic fair type rounds",
    },
    counts = new { candidates = candidates.Count, targets = targets.Values.Sum(), assigned = selected.Length },
    perType = targets.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(
        x => x.Key,
        x => new { candidates = candidates.Count(c => c.Prefab == x.Key), target = x.Value, assigned = assigned.GetValueOrDefault(x.Key) },
        StringComparer.Ordinal),
    actualPairwiseMinimum = pairwiseMinimum,
    warnings = selection.Warnings,
    selectedCandidates = selected,
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine(JsonSerializer.Serialize(new { output, document.counts, pairwiseMinimum, worldIdentity }));
return 0;
