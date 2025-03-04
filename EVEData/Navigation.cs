﻿namespace SMT.EVEData
{
    public enum RoutingMode
    {
        Shortest,
        Safest,
        PreferLow,
    }

    public class Navigation
    {
        public enum GateType
        {
            StarGate,
            Ansiblex,
            JumpTo,
            Thera,
        }

        private static Dictionary<string, MapNode> MapNodes { get; set; }
        private static List<string> TheraLinks { get; set; }

        public static void ClearJumpBridges()
        {
            foreach (MapNode mn in MapNodes.Values)
            {
                mn.JBConnection = null;
            }
        }

        public static void ClearTheraConnections()
        {
            foreach (MapNode mn in MapNodes.Values)
            {
                mn.TheraConnections = null;
            }
        }

        public static void UpdateTheraConnections(List<string> theraSystems)
        {
            ClearTheraConnections();

            foreach (string ts in theraSystems)
            {
                MapNodes[ts].TheraConnections = theraSystems;
            }
        }

        public static List<string> GetSystemsWithinXLYFrom(string start, double LY, bool includeHighSecSystems, bool includePochvenSystems)
        {
            List<string> inRange = new List<string>();

            MapNode startSys = null;

            foreach (MapNode sys in MapNodes.Values)
            {
                if (sys.Name == start)
                {
                    startSys = sys;
                    break;
                }
            }

            foreach (MapNode sys in MapNodes.Values)
            {
                if (sys == startSys)
                {
                    continue;
                }

                double x = startSys.X - sys.X;
                double y = startSys.Y - sys.Y;
                double z = startSys.Z - sys.Z;

                double length = Math.Sqrt((x * x) + (y * y) + (z * z)) / 9460730472580800.0;

                bool shouldAdd = false;

                if (length < LY)
                {
                    shouldAdd = true;
                }

                if (sys.HighSec & !includeHighSecSystems)
                {
                    shouldAdd = false;
                }

                if (sys.Pochven & !includePochvenSystems)
                {
                    shouldAdd = false;
                }

                if (shouldAdd)
                {
                    inRange.Add(sys.Name);
                }
            }

            return inRange;
        }

        public static List<string> GetSystemsXJumpsFrom(List<string> sysList, string start, int X)
        {
            if (MapNodes == null || !MapNodes.ContainsKey(start))
            {
                return sysList;
            }

            if (X != 0)
            {
                if (!sysList.Contains(start))
                {
                    sysList.Add(start);
                }

                MapNode mn = MapNodes[start];

                foreach (string mm in mn.Connections)
                {
                    if (!sysList.Contains(mm))
                    {
                        sysList.Add(mm);
                    }

                    List<string> connected = GetSystemsXJumpsFrom(sysList, mm, X - 1);
                    foreach (string s in connected)
                    {
                        if (!sysList.Contains(s))
                        {
                            sysList.Add(s);
                        }
                    }
                }
            }
            return sysList;
        }

        public static void InitNavigation(List<System> eveSystems, List<JumpBridge> jumpBridges)
        {
            MapNodes = new Dictionary<string, MapNode>();

            TheraLinks = new List<string>();

            // build up the nav structures
            foreach (System sys in eveSystems)
            {
                MapNode mn = new MapNode
                {
                    Name = sys.Name,
                    HighSec = sys.TrueSec > 0.45,
                    Pochven = sys.Region == "Pochven",
                    Connections = new List<string>(),
                    JumpableSystems = new List<JumpLink>(),
                    Cost = 1,
                    MinCostToStart = 0,
                    X = sys.ActualX,
                    Y = sys.ActualY,
                    Z = sys.ActualZ,
                    F = 0,
                    ActualSystem = sys
                };

                foreach (string s in sys.Jumps)
                {
                    mn.Connections.Add(s);
                }

                MapNodes[mn.Name] = mn;
            }

            UpdateJumpBridges(jumpBridges);

            double MaxRange = 10 * 9460730472580800.0;

            // now create the jumpable system links
            foreach (MapNode mn in MapNodes.Values)
            {
                foreach (System sys in eveSystems)
                {
                    // cant jump into highsec systems
                    if (sys.TrueSec > 0.45)
                    {
                        continue;
                    }

                    // cant jump into Pochven systems
                    if (sys.Region == "Pochven")
                    {
                        continue;
                    }

                    double Distance = EveManager.Instance.GetRangeBetweenSystems(sys.Name, mn.Name);
                    if (Distance < MaxRange && Distance > 0)
                    {
                        JumpLink jl = new JumpLink();
                        jl.System = sys.Name;
                        jl.RangeLY = Distance / 9460730472580800.0;
                        mn.JumpableSystems.Add(jl);
                    }
                }
            }
        }

        public static List<RoutePoint> Navigate(string From, string To, bool UseJumpGates, bool UseThera, RoutingMode routingMode)
        {
            if (!(MapNodes.ContainsKey(From)) || !(MapNodes.ContainsKey(To)) || From == "" || To == "")

            {
                return null;
            }

            // clear the scores, values and parents from the list
            foreach (MapNode mapNode in MapNodes.Values)
            {
                mapNode.NearestToStart = null;
                mapNode.MinCostToStart = 0;
                mapNode.Visited = false;

                switch (routingMode)
                {
                    case RoutingMode.PreferLow:
                        {
                            if (mapNode.HighSec)
                                mapNode.Cost = 1000;
                        }
                        break;

                    case RoutingMode.Safest:
                        {
                            if (!mapNode.HighSec)
                                mapNode.Cost = 1000;
                        }
                        break;

                    case RoutingMode.Shortest:
                        mapNode.Cost = 1;
                        break;
                }
            }

            MapNode Start = MapNodes[From];
            MapNode End = MapNodes[To];

            List<MapNode> OpenList = new List<MapNode>();
            List<MapNode> ClosedList = new List<MapNode>();

            MapNode CurrentNode = null;

            // add the start to the open list
            OpenList.Add(Start);

            while (OpenList.Count > 0)
            {
                // get the MapNode with the lowest F score
                double lowest = OpenList.Min(mn => mn.MinCostToStart);
                CurrentNode = OpenList.First(mn => mn.MinCostToStart == lowest);

                // add the list to the closed list
                ClosedList.Add(CurrentNode);

                // remove it from the open list
                OpenList.Remove(CurrentNode);

                // walk the connections
                foreach (string connectionName in CurrentNode.Connections)
                {
                    MapNode CMN = MapNodes[connectionName];

                    if (CMN.Visited)
                        continue;

                    if (CMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + CMN.Cost < CMN.MinCostToStart)
                    {
                        CMN.MinCostToStart = CurrentNode.MinCostToStart + CMN.Cost;
                        CMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(CMN))
                        {
                            OpenList.Add(CMN);
                        }
                    }
                }

                if (UseJumpGates && CurrentNode.JBConnection != null)
                {
                    MapNode JMN = MapNodes[CurrentNode.JBConnection];
                    if (!JMN.Visited && JMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + JMN.Cost < JMN.MinCostToStart)
                    {
                        JMN.MinCostToStart = CurrentNode.MinCostToStart + JMN.Cost;
                        JMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(JMN))
                        {
                            OpenList.Add(JMN);
                        }
                    }
                }

                if (UseThera && CurrentNode.TheraConnections != null)
                {
                    foreach (string theraConnection in CurrentNode.TheraConnections)
                    {
                        MapNode CMN = MapNodes[theraConnection];

                        if (CMN.Visited)
                            continue;

                        if (CMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + CMN.Cost < CMN.MinCostToStart)
                        {
                            CMN.MinCostToStart = CurrentNode.MinCostToStart + CMN.Cost;
                            CMN.NearestToStart = CurrentNode;
                            if (!OpenList.Contains(CMN))
                            {
                                OpenList.Add(CMN);
                            }
                        }
                    }
                }

                /* Todo :  Additional error checking
                if (UseThera && !string.IsNullOrEmptyCurrent(Node.TheraInSig))
                {
                    //SJS HERE ERROR
                }
                */

                CurrentNode.Visited = true;
            }

            // build the path

            List<string> Route = new List<string>();

            bool rootError = false;

            CurrentNode = End;
            if (End.NearestToStart != null)
            {
                while (CurrentNode != null)
                {
                    Route.Add(CurrentNode.Name);
                    CurrentNode = CurrentNode.NearestToStart;
                    if (Route.Count > 2000)
                    {
                        rootError = true;
                        break;
                    }
                }
                Route.Reverse();
            }

            List<RoutePoint> ActualRoute = new List<RoutePoint>();

            if (!rootError)
            {
                for (int i = 0; i < Route.Count; i++)
                {
                    RoutePoint RP = new RoutePoint();
                    RP.SystemName = Route[i];
                    RP.ActualSystem = EveManager.Instance.GetEveSystem(Route[i]);
                    RP.GateToTake = GateType.StarGate;
                    RP.LY = 0.0;

                    if (i < Route.Count - 1)
                    {
                        MapNode mn = MapNodes[RP.SystemName];
                        if (mn.JBConnection != null && mn.JBConnection == Route[i + 1])
                        {
                            RP.GateToTake = GateType.Ansiblex;
                        }

                        if (UseThera && mn.TheraConnections != null && mn.TheraConnections.Contains(Route[i + 1]))
                        {
                            RP.GateToTake = GateType.Thera;
                        }
                    }
                    ActualRoute.Add(RP);
                }
            }

            return ActualRoute;
        }

        public static List<RoutePoint> NavigateCapitals(string From, string To, double MaxLY, LocalCharacter lc, List<string> systemsToAvoid)
        {
            if (!(MapNodes.ContainsKey(From)) || !(MapNodes.ContainsKey(To)) || From == "" || To == "")
            {
                return null;
            }

            double ExtraJumpFactor = 5.0;
            double AvoidFactor = 0.0;

            // clear the scores, values and parents from the list
            foreach (MapNode mapNode in MapNodes.Values)
            {
                mapNode.NearestToStart = null;
                mapNode.MinCostToStart = 0;
                mapNode.Visited = false;
            }

            MapNode Start = MapNodes[From];
            MapNode End = MapNodes[To];

            List<MapNode> OpenList = new List<MapNode>();
            List<MapNode> ClosedList = new List<MapNode>();

            MapNode CurrentNode = null;

            // add the start to the open list
            OpenList.Add(Start);

            while (OpenList.Count > 0)
            {
                // get the MapNode with the lowest F score
                double lowest = OpenList.Min(mn => mn.MinCostToStart);
                CurrentNode = OpenList.First(mn => mn.MinCostToStart == lowest);

                // add the list to the closed list
                ClosedList.Add(CurrentNode);

                // remove it from the open list
                OpenList.Remove(CurrentNode);

                // walk the connections
                foreach (JumpLink connection in CurrentNode.JumpableSystems)
                {
                    if (connection.RangeLY > MaxLY)
                    {
                        continue;
                    }

                    MapNode CMN = MapNodes[connection.System];

                    if (CMN.Visited)
                        continue;

                    if (systemsToAvoid.Contains(connection.System))
                    {
                        AvoidFactor = 10000;
                    }
                    else
                    {
                        AvoidFactor = 0.0;
                    }

                    if (CMN.MinCostToStart == 0 || CurrentNode.MinCostToStart + connection.RangeLY + ExtraJumpFactor + AvoidFactor < CMN.MinCostToStart)
                    {
                        CMN.MinCostToStart = CurrentNode.MinCostToStart + connection.RangeLY + ExtraJumpFactor + AvoidFactor;
                        CMN.NearestToStart = CurrentNode;
                        if (!OpenList.Contains(CMN))
                        {
                            OpenList.Add(CMN);
                        }
                    }
                }

                CurrentNode.Visited = true;
            }

            // build the path

            List<string> Route = new List<string>();

            CurrentNode = End;
            if (End.NearestToStart != null)
            {
                while (CurrentNode != null)
                {
                    Route.Add(CurrentNode.Name);
                    CurrentNode = CurrentNode.NearestToStart;
                }
            }

            List<RoutePoint> ActualRoute = new List<RoutePoint>();

            for (int i = 0; i < Route.Count; i++)
            {
                RoutePoint RP = new RoutePoint();
                RP.GateToTake = GateType.JumpTo;
                RP.LY = 0.0;
                RP.SystemName = Route[i];

                if (i > 0)
                {
                    RP.LY = EveManager.Instance.GetRangeBetweenSystems(Route[i], Route[i - 1]) / 9460730472580800.0;
                }
                ActualRoute.Add(RP);
            }

            ActualRoute.Reverse();

            return ActualRoute;
        }

        public static void UpdateJumpBridges(List<JumpBridge> jumpBridges)
        {
            foreach (JumpBridge jb in jumpBridges)
            {
                if (jb.Disabled)
                {
                    continue;
                }

                MapNodes[jb.From].JBConnection = jb.To;
                MapNodes[jb.To].JBConnection = jb.From;
            }
        }

        public static void UpdateTheraInfo(List<TheraConnection> theraList)
        {
            TheraLinks.Clear();
            foreach (MapNode mapNode in MapNodes.Values)
            {
                mapNode.TheraInSig = string.Empty;
                mapNode.TheraOutSig = string.Empty;
            }

            foreach (TheraConnection tc in theraList)
            {
                MapNode mn = MapNodes[tc.System];
                mn.TheraInSig = tc.InSignatureID;
                mn.TheraOutSig = tc.OutSignatureID;

                TheraLinks.Add(tc.System);
            }
        }

        private struct JumpLink
        {
            public double RangeLY;
            public string System;
        }

        public class RoutePoint
        {
            public GateType GateToTake { get; set; }
            public double LY { get; set; }
            public string SystemName { get; set; }

            public System ActualSystem { get; set; }

            public override string ToString()
            {
                string s = SystemName;
                if (GateToTake == GateType.Ansiblex)
                {
                    s += " (Ansiblex)";
                }

                if (GateToTake == GateType.Thera)
                {
                    s += " (Thera)";
                }

                if (GateToTake == GateType.JumpTo && LY > 0.0)
                {
                    s += " (Jump To, Range " + LY.ToString("0.##") + " )";
                }

                return s;
            }
        }

        private class MapNode
        {
            public double Cost;
            public double F;
            public string JBConnection;
            public List<string> TheraConnections;
            public double MinCostToStart;
            public MapNode NearestToStart;
            public string TheraInSig;
            public string TheraOutSig;
            public bool Visited;
            public double X;
            public double Y;
            public double Z;
            public List<string> Connections { get; set; }
            public bool HighSec { get; set; }
            public bool Pochven { get; set; }
            public List<JumpLink> JumpableSystems { get; set; }
            public string Name { get; set; }
            public System ActualSystem { get; set; }
        }
    }
}