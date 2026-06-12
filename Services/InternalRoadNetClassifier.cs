using Game.Net;
using Game.Prefabs;

namespace ChangeInternalRoads.Services
{
    internal sealed class InternalRoadNetClassifier
    {
        public NetClassification Classify(NetPrefab netPrefab)
        {
            if (netPrefab == null)
            {
                return NetClassification.Unsupported(InternalRoadSkipReason.NullNetPrefab, "netPrefab=<null>");
            }

            if (netPrefab is not NetGeometryPrefab geometryPrefab)
            {
                return NetClassification.Unsupported(InternalRoadSkipReason.NonGeometryNetPrefab, $"netPrefab={netPrefab.name} type={netPrefab.GetType().Name}");
            }

            RoadSubnetProfile profile = BuildProfile(geometryPrefab);
            return NetClassification.Supported(profile.Format());
        }

        private static RoadSubnetProfile BuildProfile(NetGeometryPrefab geometryPrefab)
        {
            var profile = new RoadSubnetProfile
            {
                PrefabName = geometryPrefab.name
            };

            if (geometryPrefab.m_Sections == null)
            {
                profile.Source = "NetGeometryPrefab.sections=missing";
                return profile;
            }

            profile.Source = $"NetGeometryPrefab.sections={geometryPrefab.m_Sections.Length}";
            for (var sectionIndex = 0; sectionIndex < geometryPrefab.m_Sections.Length; sectionIndex++)
            {
                var sectionInfo = geometryPrefab.m_Sections[sectionIndex];
                if (sectionInfo.m_Section?.m_Pieces == null)
                {
                    continue;
                }

                for (var pieceIndex = 0; pieceIndex < sectionInfo.m_Section.m_Pieces.Length; pieceIndex++)
                {
                    var pieceInfo = sectionInfo.m_Section.m_Pieces[pieceIndex];
                    if (pieceInfo.m_Piece == null ||
                        !pieceInfo.m_Piece.TryGet(out NetPieceLanes pieceLanes) ||
                        pieceLanes.m_Lanes == null)
                    {
                        continue;
                    }

                    for (var laneIndex = 0; laneIndex < pieceLanes.m_Lanes.Length; laneIndex++)
                    {
                        var laneInfo = pieceLanes.m_Lanes[laneIndex];
                        if (laneInfo.m_Lane == null)
                        {
                            continue;
                        }

                        AccumulateLane(laneInfo.m_Lane, ref profile);
                    }
                }
            }

            return profile;
        }

        private static void AccumulateLane(PrefabBase lanePrefab, ref RoadSubnetProfile profile)
        {
            if (lanePrefab.TryGet(out Game.Prefabs.CarLane carLane) && carLane.active)
            {
                if ((carLane.m_RoadType & RoadTypes.Car) != 0)
                {
                    profile.ActiveCarLaneCount++;
                }

                if ((carLane.m_RoadType & RoadTypes.Bicycle) != 0)
                {
                    profile.BicycleLaneCount++;
                }
            }

            if (lanePrefab.TryGet(out Game.Prefabs.PedestrianLane pedestrianLane) && pedestrianLane.active)
            {
                profile.PedestrianLaneCount++;
            }

            if (lanePrefab.TryGet(out Game.Prefabs.TrackLane trackLane) && trackLane.active)
            {
                profile.TrackLaneCount++;
            }

            if (lanePrefab.TryGet(out Game.Prefabs.UtilityLane utilityLane) && utilityLane.active)
            {
                profile.UtilityLaneCount++;
            }
        }
    }

    internal struct NetClassification
    {
        public bool IsSupported;

        public InternalRoadSkipReason SkipReason;

        public string Detail;

        public static NetClassification Supported(string detail)
        {
            return new NetClassification
            {
                IsSupported = true,
                SkipReason = InternalRoadSkipReason.None,
                Detail = detail
            };
        }

        public static NetClassification Unsupported(InternalRoadSkipReason reason, string detail)
        {
            return new NetClassification
            {
                IsSupported = false,
                SkipReason = reason,
                Detail = detail
            };
        }
    }

    internal struct RoadSubnetProfile
    {
        public string PrefabName;

        public string Source;

        public int ActiveCarLaneCount;

        public int BicycleLaneCount;

        public int PedestrianLaneCount;

        public int TrackLaneCount;

        public int UtilityLaneCount;

        public string Format()
        {
            return $"netPrefab={PrefabName} source={Source} car={ActiveCarLaneCount} bicycle={BicycleLaneCount} pedestrian={PedestrianLaneCount} track={TrackLaneCount} utility={UtilityLaneCount}";
        }
    }

    internal enum InternalRoadSkipReason
    {
        None = 0,
        NullNetPrefab = 1,
        NonGeometryNetPrefab = 2
    }
}
