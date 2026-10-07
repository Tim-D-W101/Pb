using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>
/// Builds a <see cref="LevelLayout"/> from a level file and the kit: places buildings, wall runs and
/// props, then resolves spawns, patrol routes, areas and pickups. Reference errors name the level
/// file and key; geometry inside kit files was already checked when the kit loaded.
/// </summary>
public static class LevelFactory
{
    /// <summary>Thickness of the invisible walking walls along the level bounds.</summary>
    private const float BoundaryThickness = 1f;

    public static LevelLayout Build(LevelDef def, string file, KitCatalog kit)
    {
        var errors = new Validator(file);
        var sink = new PrimitiveSink { DoorKinds = kit.Doors };
        var owners = new List<string>();
        var props = new List<PropInstance>();
        var buildings = new List<PlacedBuilding>();
        var walls = new List<PlacedWall>();
        var areas = new List<AreaSpec>();
        var materials = kit.Materials.ToDictionary(m => m.Id, StringComparer.Ordinal);

        int Owner(string name)
        {
            owners.Add(name);
            return owners.Count - 1;
        }

        MaterialRef Material(Validator scope, string property, string? id) => KitCatalog.ResolveMaterial(materials, scope, property, id);

        void PlaceProp(Validator scope, PropPlacementDef placement, PlanFrame parent, string ownerPrefix)
        {
            if (!kit.Props.TryGetValue(placement.Prop, out PropType? type))
            {
                scope.Error(nameof(PropPlacementDef.Prop), $"unknown prop '{placement.Prop}'");
                return;
            }

            Vector3 position = parent.ToWorld(Validator.ToVector3(placement.Position_m));
            float yaw = parent.Yaw + placement.Yaw_deg * Units.DegreesToRadians;
            int first = sink.Items.Count;
            KitGeometry.Prop(sink, new PlanFrame(position, yaw), type, Owner($"{ownerPrefix}prop:{type.Id}#{props.Count}"));
            props.Add(new PropInstance
            {
                Type = type,
                Position = position,
                Yaw = yaw,
                FirstPrimitive = first,
                PrimitiveCount = sink.Items.Count - first,
            });
        }

        // Ground.
        if (!materials.TryGetValue(def.GroundMaterial, out KitMaterial? ground))
        {
            errors.Error(nameof(LevelDef.GroundMaterial), $"unknown material '{def.GroundMaterial}'");
            ground = kit.Materials[0];
        }

        for (int i = 0; def.GroundPatches is not null && i < def.GroundPatches.Length; i++)
        {
            GroundPatchDef patch = def.GroundPatches[i];
            Validator item = errors.Item(nameof(LevelDef.GroundPatches), i);
            MaterialRef m = Material(item, nameof(GroundPatchDef.Material), patch.Material);
            // A thin slab just above the ground: paint sees its surface, walking doesn't notice the 4 mm.
            KitGeometry.Slab(sink, PlanFrame.Identity, KitGeometry.Rect(patch.Rect_m), Array.Empty<Vector4>(), 0.004f, 0.05f, m,
                PrimitiveFlags.Paint | PrimitiveFlags.Render, PrimitiveRole.GroundPatch, Owner($"patch#{i}"));
        }

        // Buildings.
        for (int i = 0; i < def.Buildings.Length; i++)
        {
            BuildingPlacementDef placement = def.Buildings[i];
            Validator item = errors.Item(nameof(LevelDef.Buildings), i);
            if (!kit.Buildings.TryGetValue(placement.Building, out BuildingTemplate? template))
            {
                item.Error(nameof(BuildingPlacementDef.Building), $"unknown building '{placement.Building}'");
                continue;
            }

            var frame = new PlanFrame(Validator.ToVector3(placement.Position_m), placement.Yaw_deg * Units.DegreesToRadians);
            int owner = Owner($"{template.Id}#{i}");
            buildings.Add(new PlacedBuilding(template, owner, frame));
            BuildBuilding(sink, frame, template.Def, materials, owner, areas, (scope, p) => PlaceProp(scope, p, frame, $"{template.Id}#{i}/"));
        }

        // Free-standing wall runs (perimeter, yard walls).
        for (int i = 0; def.Walls is not null && i < def.Walls.Length; i++)
        {
            WallDef wall = def.Walls[i];
            Validator item = errors.Item(nameof(LevelDef.Walls), i);
            MaterialRef m = Material(item, nameof(WallDef.Material), wall.Material);
            KitGeometry.CheckWall(wall, wall.Height_m, item);
            KitCatalog.CheckLeaves(wall, kit.Doors, item);
            if (wall.Dressing is { } dressing)
            {
                Validator dressed = item.Scope(nameof(WallDef.Dressing));
                foreach ((string key, string? id) in new[] { (nameof(WallDressingDef.PierMaterial), dressing.PierMaterial),
                             (nameof(WallDressingDef.Coping), dressing.Coping), (nameof(WallDressingDef.Wire), dressing.Wire) })
                {
                    if (id is not null)
                    {
                        Material(dressed, key, id);
                    }
                }
            }

            if (!float.IsNaN(wall.BaseElevation_m) && !float.IsNaN(wall.Height_m))
            {
                int owner = Owner($"wall#{i}");
                walls.Add(new PlacedWall(wall, owner, PlanFrame.Identity));
                KitGeometry.WallRun(sink, PlanFrame.Identity, wall, wall.BaseElevation_m, wall.Height_m, m, owner);
            }
        }

        // Scenery beyond the level: only its materials matter here.
        foreach ((string key, LineDef[]? lines) in new[] { (nameof(SceneryDef.PowerLines), def.Scenery?.PowerLines), (nameof(SceneryDef.PoleLines), def.Scenery?.PoleLines) })
        {
            for (int i = 0; lines is not null && i < lines.Length; i++)
            {
                Validator item = errors.Scope(nameof(LevelDef.Scenery)).Item(key, i);
                Material(item, nameof(LineDef.Material), lines[i].Material);
                Material(item, nameof(LineDef.Wire), lines[i].Wire);
            }
        }

        // Props.
        for (int i = 0; def.Props is not null && i < def.Props.Length; i++)
        {
            PlaceProp(errors.Item(nameof(LevelDef.Props), i), def.Props[i], PlanFrame.Identity, string.Empty);
        }

        // Tracks: two rails along each, paint only (feet step over them); the game draws them and their sleepers.
        var tracks = new List<TrackSpec>();
        for (int i = 0; def.Tracks is not null && i < def.Tracks.Length; i++)
        {
            TrackDef t = def.Tracks[i];
            Validator item = errors.Item(nameof(LevelDef.Tracks), i);
            MaterialRef rail = Material(item, nameof(TrackDef.RailMaterial), t.RailMaterial);
            MaterialRef sleeper = Material(item, nameof(TrackDef.SleeperMaterial), t.SleeperMaterial);
            if (t.Points_m.Length < 2 || t.Points_m.Any(p => p is not { Length: 2 }))
            {
                continue; // already reported
            }

            Vector3[] points = t.Points_m.Select(p => new Vector3(p[0], t.Elevation_m, p[1])).ToArray();
            int owner = Owner($"track {i + 1}");
            for (int k = 1; k < points.Length; k++)
            {
                Vector3 a = points[k - 1], b = points[k];
                Vector3 along = b - a;
                float length = along.Length();
                if (length < 1e-3f)
                {
                    continue;
                }

                along /= length;
                var across = new Vector3(-along.Z, 0f, along.X);
                Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(along.X, along.Z));
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 centre = (a + b) * 0.5f + across * (side * (t.Gauge_m + t.RailWidth_m) * 0.5f) + new Vector3(0f, t.RailHeight_m * 0.5f, 0f);
                    sink.AddBox(PlanFrame.Identity, centre, turn, new Vector3(t.RailWidth_m * 0.5f, t.RailHeight_m * 0.5f, length * 0.5f), rail,
                        PrimitiveFlags.Paint, PrimitiveRole.Rail, owner);
                }
            }

            tracks.Add(new TrackSpec
            {
                Points = points, Gauge = t.Gauge_m, RailHeight = t.RailHeight_m, RailWidth = t.RailWidth_m,
                RailMaterial = kit.Materials[rail.Index], SleeperMaterial = kit.Materials[sleeper.Index],
            });
        }

        // Invisible walking walls along the bounds.
        var bounds = new Aabb(Validator.ToVector3(def.BoundsMin_m), Validator.ToVector3(def.BoundsMax_m));
        AddBoundary(sink, bounds, ground.Ref, Owner("bounds"));

        // Areas, spawns, patrols, pickups.
        for (int i = 0; def.Areas is not null && i < def.Areas.Length; i++)
        {
            areas.Add(ToArea(def.Areas[i], PlanFrame.Identity));
        }

        var patrols = new Dictionary<string, PatrolRoute>(StringComparer.Ordinal);
        for (int i = 0; def.Patrols is not null && i < def.Patrols.Length; i++)
        {
            PatrolDef p = def.Patrols[i];
            patrols[p.Id] = new PatrolRoute
            {
                Id = p.Id,
                Points = p.Points_m.Select(Validator.ToVector3).ToArray(),
                Loop = p.Loop,
                Pause = p.Pause_s,
            };
            for (int k = 0; k < p.Points_m.Length; k++)
            {
                CheckInside(errors.Item(nameof(LevelDef.Patrols), i), nameof(PatrolDef.Points_m), Validator.ToVector3(p.Points_m[k]), bounds);
            }
        }

        var spawns = new List<OpponentSpawn>();
        for (int i = 0; i < def.OpponentSpawns.Length; i++)
        {
            OpponentSpawnDef s = def.OpponentSpawns[i];
            Validator item = errors.Item(nameof(LevelDef.OpponentSpawns), i);
            PatrolRoute? route = null;
            if (!string.IsNullOrEmpty(s.Patrol) && !patrols.TryGetValue(s.Patrol, out route))
            {
                item.Error(nameof(OpponentSpawnDef.Patrol), $"unknown patrol '{s.Patrol}'");
            }

            Vector3 position = Validator.ToVector3(s.Position_m);
            CheckInside(item, nameof(OpponentSpawnDef.Position_m), position, bounds);
            spawns.Add(new OpponentSpawn
            {
                Id = s.Id,
                Position = position,
                Yaw = s.Yaw_deg * Units.DegreesToRadians,
                Roles = s.Roles,
                Patrol = route,
            });
        }

        var pickups = new List<PickupSpec>();
        for (int i = 0; def.Pickups is not null && i < def.Pickups.Length; i++)
        {
            PickupDef p = def.Pickups[i];
            Vector3 position = Validator.ToVector3(p.Position_m);
            CheckInside(errors.Item(nameof(LevelDef.Pickups), i), nameof(PickupDef.Position_m), position, bounds);
            pickups.Add(new PickupSpec { Id = p.Id, Kind = p.Kind, Position = position });
        }

        var playerSpawns = new List<SpawnPoint>();
        for (int i = 0; i < def.PlayerSpawns.Length; i++)
        {
            Vector3 position = Validator.ToVector3(def.PlayerSpawns[i].Position_m);
            CheckInside(errors.Item(nameof(LevelDef.PlayerSpawns), i), nameof(SpawnDef.Position_m), position, bounds);
            playerSpawns.Add(new SpawnPoint(position, def.PlayerSpawns[i].Yaw_deg * Units.DegreesToRadians));
        }

        AreaSpec spawnArea = areas.First(a => a.Name == def.SpawnArea);
        LevelObjectives objectives = def.Objectives is null ? LevelObjectives.None
            : ToObjectives(errors.Scope(nameof(LevelDef.Objectives)), def.Objectives, areas, playerSpawns, bounds);
        Vector3 deadZone = Validator.ToVector3(def.DeadZone_m);
        Viewpoint[] viewpoints = (def.Viewpoints ?? Array.Empty<ViewpointDef>())
            .Select(vp => new Viewpoint(vp.Name, Validator.ToVector3(vp.Position_m), vp.Yaw_deg * Units.DegreesToRadians, vp.Pitch_deg * Units.DegreesToRadians))
            .ToArray();
        var places = new List<PlaceSpec>();
        for (int i = 0; i < def.Places.Length; i++)
        {
            places.Add(ToPlace(def.Places[i], errors.Item(nameof(LevelDef.Places), i), bounds, playerSpawns, deadZone, viewpoints));
        }

        errors.ThrowIfErrors();

        return new LevelLayout
        {
            Id = def.Id,
            DisplayName = def.DisplayName,
            Description = def.Description,
            Bounds = bounds,
            GroundMaterial = ground,
            Materials = kit.Materials,
            Primitives = sink.Items,
            Apertures = sink.Apertures,
            Doors = sink.Doors,
            Props = props,
            Buildings = buildings,
            Walls = walls,
            Scenery = def.Scenery,
            Markings = def.Markings,
            Owners = owners,
            PlayerSpawns = playerSpawns,
            SpawnArea = spawnArea.Box,
            DeadZone = Validator.ToVector3(def.DeadZone_m),
            Areas = areas,
            OpponentSpawns = spawns,
            Patrols = patrols.Values.ToArray(),
            Pickups = pickups,
            Viewpoints = viewpoints,
            Objectives = objectives,
            Tracks = tracks,
            Places = places,
            Place = places.FirstOrDefault(p => p.Whole),
        };
    }

    /// <summary>A place from its definition: its rect inside the level, with an entry, a walk-off spot and the viewpoint of its picture inside it.</summary>
    private static PlaceSpec ToPlace(PlaceDef d, Validator item, Aabb bounds, List<SpawnPoint> levelEntries, Vector3 levelDeadZone,
        IReadOnlyList<Viewpoint> viewpoints)
    {
        Aabb? rect = null;
        if (d.Rect_m is { Length: 4 } r && r[2] > r[0] && r[3] > r[1])
        {
            rect = new Aabb(new Vector3(r[0], bounds.Min.Y, r[1]), new Vector3(r[2], bounds.Max.Y, r[3]));
            if (r[0] < bounds.Min.X || r[2] > bounds.Max.X || r[1] < bounds.Min.Z || r[3] > bounds.Max.Z)
            {
                item.Error(nameof(PlaceDef.Rect_m), "reaches outside the level bounds");
            }
        }

        var place = new PlaceSpec
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Description = d.Description,
            Bounds = rect,
            PlayerSpawns = d.PlayerSpawns?.Select(s => new SpawnPoint(Validator.ToVector3(s.Position_m), s.Yaw_deg * Units.DegreesToRadians)).ToArray(),
            DeadZone = d.DeadZone_m is { Length: 3 } z ? Validator.ToVector3(z) : null,
            SpawnScale = d.SpawnScale,
            Still = StillOf(d, item, rect, viewpoints),
        };

        IReadOnlyList<SpawnPoint> entries = place.PlayerSpawns ?? levelEntries.Where(s => place.Contains(s.Position)).ToArray();
        if (entries.Count == 0)
        {
            item.Error(nameof(PlaceDef.PlayerSpawns), "no entry inside the place: give it playerSpawns of its own");
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (!place.Contains(entries[i].Position))
            {
                item.Item(nameof(PlaceDef.PlayerSpawns), i).Error(nameof(SpawnDef.Position_m), "is outside the place's rect");
            }
        }

        if (!place.Contains(place.DeadZone ?? levelDeadZone))
        {
            item.Error(nameof(PlaceDef.DeadZone_m), place.DeadZone is null ? "needed: the level's dead zone is outside the place's rect" : "is outside the place's rect");
        }

        return place;
    }

    /// <summary>The viewpoint a place's picture is taken from: the one it names (inside it), or the first inside it.</summary>
    private static Viewpoint StillOf(PlaceDef d, Validator item, Aabb? rect, IReadOnlyList<Viewpoint> viewpoints)
    {
        bool Inside(Viewpoint v) => rect is not { } r || (v.Position.X >= r.Min.X && v.Position.X <= r.Max.X && v.Position.Z >= r.Min.Z && v.Position.Z <= r.Max.Z);
        if (d.Still is { } name)
        {
            foreach (Viewpoint v in viewpoints)
            {
                if (v.Name == name)
                {
                    if (!Inside(v))
                    {
                        item.Error(nameof(PlaceDef.Still), $"'{name}' is outside the place's rect");
                    }

                    return v;
                }
            }

            item.Error(nameof(PlaceDef.Still), $"names no viewpoint of the level ('{name}')");
            return default;
        }

        foreach (Viewpoint v in viewpoints)
        {
            if (Inside(v))
            {
                return v;
            }
        }

        item.Error(nameof(PlaceDef.Still), "no viewpoint inside the place for its picture in the menu: add one, or name one");
        return default;
    }

    /// <summary>
    /// The level's objectives: each case spot with the biggest indoor area round it (its building, or that storey of it),
    /// the ways out (by default one at each player spawn), and each room to hold from the areas of its name.
    /// </summary>
    private static LevelObjectives ToObjectives(Validator errors, LevelObjectivesDef def, IReadOnlyList<AreaSpec> areas,
        IReadOnlyList<SpawnPoint> playerSpawns, Aabb bounds)
    {
        var spots = new List<CaseSpot>();
        for (int i = 0; i < def.CaseSpots_m.Length; i++)
        {
            Vector3 at = Validator.ToVector3(def.CaseSpots_m[i]);
            string key = $"{nameof(LevelObjectivesDef.CaseSpots_m)}[{i}]";
            CheckInside(errors, key, at, bounds);
            AreaSpec? building = areas.Where(a => a.Indoor && a.Box.Contains(at + new Vector3(0f, 0.1f, 0f))).MaxBy(a => a.Volume);
            if (building is null)
            {
                errors.Error(key, $"{at} isn't inside an indoor area (the case starts in a building)");
                continue;
            }

            spots.Add(new CaseSpot(at, building.Name, building.Box));
        }

        var exits = new List<ExitSpec>();
        for (int i = 0; def.Exits is not null && i < def.Exits.Length; i++)
        {
            Vector3 at = Validator.ToVector3(def.Exits[i].At_m);
            CheckInside(errors.Item(nameof(LevelObjectivesDef.Exits), i), nameof(ExitDef.At_m), at, bounds);
            exits.Add(new ExitSpec(def.Exits[i].Name, at));
        }

        if (exits.Count == 0)
        {
            for (int i = 0; i < playerSpawns.Count; i++)
            {
                exits.Add(new ExitSpec($"way out {i + 1}", playerSpawns[i].Position));
            }
        }

        var rooms = new List<HoldRoom>();
        for (int i = 0; i < def.HoldRooms.Length; i++)
        {
            string name = def.HoldRooms[i];
            Aabb[] boxes = areas.Where(a => a.Name == name).Select(a => a.Box).ToArray();
            if (boxes.Length == 0)
            {
                errors.Error($"{nameof(LevelObjectivesDef.HoldRooms)}[{i}]", $"no area is called '{name}'");
                continue;
            }

            Aabb biggest = boxes.MaxBy(b => (b.Max.X - b.Min.X) * (b.Max.Z - b.Min.Z));
            var centre = new Vector3((biggest.Min.X + biggest.Max.X) * 0.5f, MathF.Max(biggest.Min.Y, 0f), (biggest.Min.Z + biggest.Max.Z) * 0.5f);
            rooms.Add(new HoldRoom { Name = name, Boxes = boxes, Centre = centre });
        }

        return new LevelObjectives { CaseSpots = spots, Exits = exits, Rooms = rooms };
    }

    /// <summary>
    /// Generates one building: ground-floor finish, walls by storey, upper floors, roof and parapet,
    /// stairs, columns, props and room areas.
    /// </summary>
    public static void BuildBuilding(PrimitiveSink sink, PlanFrame frame, BuildingDef b, IReadOnlyDictionary<string, KitMaterial> materials,
        int owner, List<AreaSpec> areas, Action<Validator, PropPlacementDef> placeProp)
    {
        // Kit files were checked at load, so a missing reference here is a programming error.
        var unused = new Validator("(kit)");
        MaterialRef Material(string? id) => KitCatalog.ResolveMaterial(materials, unused, "material", id);

        MaterialRef floor = Material(b.FloorMaterial);
        // Ground floor: a thin slab whose top sits 2 mm above the ground, so paint hits the floor
        // material indoors while walking never notices the step.
        KitGeometry.Slab(sink, frame, KitGeometry.Rect(b.Footprint_m), Array.Empty<Vector4>(), 0.002f, 0.1f, floor,
            PrimitiveFlags.Paint | PrimitiveFlags.Render, PrimitiveRole.Floor, owner);

        foreach (WallDef w in b.Walls)
        {
            float baseY = float.IsNaN(w.BaseElevation_m) ? b.StoreyBase(w.Storey) : w.BaseElevation_m;
            float height = float.IsNaN(w.Height_m) ? b.Storeys_m[Math.Clamp(w.Storey, 0, b.Storeys_m.Length - 1)] : w.Height_m;
            KitGeometry.WallRun(sink, frame, w, baseY, height, Material(w.Material), owner);
        }

        foreach (SlabDef s in b.Floors ?? Array.Empty<SlabDef>())
        {
            float thickness = float.IsNaN(s.Thickness_m) ? b.SlabThickness_m : s.Thickness_m;
            KitGeometry.Slab(sink, frame, KitGeometry.Rect(s.Rect_m), KitGeometry.Rects(s.Holes_m), s.Elevation_m, thickness,
                Material(s.Material ?? b.FloorMaterial),
                PrimitiveFlags.Paint | PrimitiveFlags.Walk | PrimitiveFlags.Render | PrimitiveFlags.Occluder, PrimitiveRole.Floor, owner);
        }

        if (b.Roof is { } roof)
        {
            float thickness = float.IsNaN(roof.Thickness_m) ? b.SlabThickness_m : roof.Thickness_m;
            float top = b.TotalHeight + thickness;
            const PrimitiveFlags roofFlags = PrimitiveFlags.Paint | PrimitiveFlags.Walk | PrimitiveFlags.Render | PrimitiveFlags.Occluder;
            Vector4 rect = KitGeometry.Rect(roof.Rect_m);
            IReadOnlyList<Vector4> holes = KitGeometry.Rects(roof.Holes_m);
            if (roof.CeilingMaterial is { } ceiling)
            {
                // A 2 cm skin of roofing on top of a structural slab that shows from inside.
                const float skin = 0.02f;
                KitGeometry.Slab(sink, frame, rect, holes, top, skin, Material(roof.Material), roofFlags, PrimitiveRole.Roof, owner);
                KitGeometry.Slab(sink, frame, rect, holes, top - skin, thickness - skin, Material(ceiling), roofFlags, PrimitiveRole.Roof, owner);
            }
            else
            {
                KitGeometry.Slab(sink, frame, rect, holes, top, thickness, Material(roof.Material), roofFlags, PrimitiveRole.Roof, owner);
            }

            foreach (Vector4 h in holes)
            {
                float x0 = MathF.Max(h.X, rect.X), z0 = MathF.Max(h.Y, rect.Y), x1 = MathF.Min(h.Z, rect.Z), z1 = MathF.Min(h.W, rect.W);
                if (x1 > x0 && z1 > z0)
                {
                    // U along the building's x, V along its −z, so U × V points up.
                    sink.Apertures.Add(new Aperture(ApertureKind.RoofHole,
                        frame.PlanToWorld(new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f), top - thickness * 0.5f),
                        Vector3.Transform(Vector3.UnitX, frame.Rotation), Vector3.Transform(-Vector3.UnitZ, frame.Rotation),
                        (x1 - x0) * 0.5f, (z1 - z0) * 0.5f, owner));
                }
            }
            if (roof.Parapet_m > 0f)
            {
                const float parapetThickness = 0.2f;
                float inset = parapetThickness * 0.5f;
                Vector4 r = KitGeometry.Rect(roof.Rect_m);
                var outline = new[]
                {
                    new Vector2(r.X + inset, r.Y + inset), new Vector2(r.Z - inset, r.Y + inset),
                    new Vector2(r.Z - inset, r.W - inset), new Vector2(r.X + inset, r.W - inset),
                };
                KitGeometry.Wall(sink, frame, outline, true, parapetThickness, top, roof.Parapet_m,
                    Material(roof.ParapetMaterial ?? roof.Material), Array.Empty<OpeningSpec>(), owner);
            }
        }

        foreach (StairDef s in b.Stairs ?? Array.Empty<StairDef>())
        {
            KitGeometry.Stairs(sink, frame, new Vector2(s.Start_m[0], s.Start_m[1]), s.Yaw_deg * Units.DegreesToRadians,
                s.Width_m, s.Run_m, s.Rise_m, s.Steps, s.BaseElevation_m, Material(s.Material), owner);
        }

        foreach (ColumnDef c in b.Columns ?? Array.Empty<ColumnDef>())
        {
            KitGeometry.Column(sink, frame, new Vector2(c.At_m[0], c.At_m[1]), new Vector2(c.Size_m[0], c.Size_m[1]),
                c.BaseElevation_m, c.Height_m, Material(c.Material), owner);
        }

        for (int i = 0; b.Props is not null && i < b.Props.Length; i++)
        {
            placeProp(unused.Item(nameof(BuildingDef.Props), i), b.Props[i]);
        }

        for (int i = 0; b.Areas is not null && i < b.Areas.Length; i++)
        {
            areas.Add(ToArea(b.Areas[i], frame));
        }
    }

    private static AreaSpec ToArea(AreaDef a, PlanFrame frame)
    {
        Vector4 r = KitGeometry.Rect(a.Rect_m);
        Span<Vector3> corners = stackalloc Vector3[]
        {
            frame.PlanToWorld(new Vector2(r.X, r.Y), a.MinY_m), frame.PlanToWorld(new Vector2(r.Z, r.Y), a.MinY_m),
            frame.PlanToWorld(new Vector2(r.Z, r.W), a.MaxY_m), frame.PlanToWorld(new Vector2(r.X, r.W), a.MaxY_m),
        };
        Aabb box = Aabb.FromPoints(corners);
        // Rotation can mix min/max heights; restore the vertical range explicitly.
        box = new Aabb(new Vector3(box.Min.X, frame.Origin.Y + a.MinY_m, box.Min.Z), new Vector3(box.Max.X, frame.Origin.Y + a.MaxY_m, box.Max.Z));
        return new AreaSpec { Name = a.Name, Box = box, Indoor = a.Indoor, Light = a.Light, Tone = a.Tone };
    }

    private static void AddBoundary(PrimitiveSink sink, Aabb bounds, MaterialRef material, int owner)
    {
        float height = bounds.Max.Y - bounds.Min.Y;
        float cy = (bounds.Max.Y + bounds.Min.Y) * 0.5f;
        float sx = bounds.Max.X - bounds.Min.X;
        float sz = bounds.Max.Z - bounds.Min.Z;
        float h = BoundaryThickness * 0.5f;
        void Wall(Vector3 center, Vector3 half) =>
            sink.AddBox(PlanFrame.Identity, center, Quaternion.Identity, half, material, PrimitiveFlags.Walk, PrimitiveRole.Boundary, owner);

        Wall(new Vector3(bounds.Min.X - h, cy, (bounds.Min.Z + bounds.Max.Z) * 0.5f), new Vector3(h, height * 0.5f, sz * 0.5f + BoundaryThickness));
        Wall(new Vector3(bounds.Max.X + h, cy, (bounds.Min.Z + bounds.Max.Z) * 0.5f), new Vector3(h, height * 0.5f, sz * 0.5f + BoundaryThickness));
        Wall(new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, cy, bounds.Min.Z - h), new Vector3(sx * 0.5f + BoundaryThickness, height * 0.5f, h));
        Wall(new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, cy, bounds.Max.Z + h), new Vector3(sx * 0.5f + BoundaryThickness, height * 0.5f, h));
    }

    private static void CheckInside(Validator scope, string property, Vector3 position, Aabb bounds)
    {
        if (!bounds.Contains(position))
        {
            scope.Error(property, "is outside the level bounds");
        }
    }
}
