﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace Dungeons.Common
{
    /// <summary>
    /// Represents a (completed) dungeoneering map.
    /// </summary>
    public class Map
    {
        // ParentDirs[x,y] points to the parent square of (x,y).
        private readonly Direction[,] parentDirs;

        public Map(Direction[,] parentDirs)
        {
            this.parentDirs = parentDirs;
        }

        public Map(int width, int height)
        {
            parentDirs = new Direction[width, height];
        }

        public Direction this[Point p]
        {
            get => parentDirs.At(p);
            set { parentDirs[p.X, p.Y] = value; }
        }

        public int Width => parentDirs.GetLength(0);
        public int Height => parentDirs.GetLength(1);
        public int MaxRooms => Width * Height;
        public Point Base { get; set; } = MapUtils.Invalid;
        public Point Boss { get; set; } = MapUtils.Invalid;
        public SortedSet<Point> CritEndpoints { get; } = new SortedSet<Point>(new PointComparer());
        public int Roomcount => GetRooms().Count();
        public int GapCount => GetGaps().Count();

        public Point Parent(Point p) => p.Add(this[p]);

        // Return the directions of the children.
        public IEnumerable<Direction> ChildrenDirs(Point p)
        {
            return from dir in MapUtils.Directions
                   let p2 = p.Add(dir)
                   where p2.IsInRange(Width, Height) && this[p2] == dir.Flip()
                   select dir;
        }

        // If no neighbors, gap.
        public RoomType GetRoomType(Point p)
        {
            var roomType = this[p].ToRoomType();
            if (p != Base && roomType <= 0)
                return roomType;

            foreach (var dir in ChildrenDirs(p))
                roomType |= dir.ToRoomType();

            return roomType == 0 ? RoomType.Gap : roomType;
        }

        public bool IsDeadEnd(Point p, bool includeBaseAndBoss = false)
        {
            // Gap is not a dead end, don't count boss unless inlcudeBaseAndBoss is true.
            if (!IsRoom(p) || (p == Boss && !includeBaseAndBoss))
                return false;

            if (p == Base && includeBaseAndBoss)
            {
                // Base is a dead end if it has exactly one child.
                return ChildrenDirs(Base).Count() == 1;
            }

            // Check if anything has it as a parent.
            return ChildrenDirs(p).Count() == 0;
        }

        public bool IsBonusDeadEnd(Point p)
        {
            return IsDeadEnd(p) && !CritEndpoints.Contains(p);
        }

        // aka non-gap
        public bool IsRoom(Point p)
        {
            return p == Base || (p.IsInRange(Width, Height) && this[p] > Direction.None);
        }

        public void AddCritEndpoint(Point p)
        {
            CritEndpoints.Add(p);
        }

        // Precondition: p is in CritEndpoints.
        public void BacktrackCritEndpoint(Point p)
        {
            if (CritEndpoints.Remove(p))
            {
                AddCritEndpoint(Parent(p));
            }
        }

        public List<Point> GetDeadEnds(bool includeBaseAndBoss = false)
        {
            return (from p in MapUtils.Range2D(Width, Height)
                    where IsDeadEnd(p, includeBaseAndBoss)
                    select p).ToList();
        }

        public List<Point> GetBonusDeadEnds()
        {
            return (from p in MapUtils.Range2D(Width, Height)
                    where IsBonusDeadEnd(p)
                    select p).ToList();
        }

        public int DistanceToBase(Point p)
        {
            int dist = 0;
            TraverseToBase(p, _ => ++dist);
            return dist;
        }

        public void TraverseToBase(Point p, Action<Point> callback)
        {
            // Prevent infinite loops
            for (int i = 0; p != Base && i < Width * Height; i++, p = Parent(p))
            {
                callback(p);
            }
        }

        public void TraverseSubtree(Point p, Action<Point, int> callback)
        {
            void Visit(Point p2, int depth)
            {
                callback(p2, depth);
                foreach (var d in ChildrenDirs(p2))
                    Visit(p2.Add(d), depth + 1);
            }

            Visit(p, 0);
        }

        // Traverses the entire map using the specified root.
        // callback takes parameters point, direction to previous point, and depth.
        public void TraverseWholeTree(Point root, Action<Point, Direction, int> callback)
        {
            if (!root.IsInRange(Width, Height))
                return;

            HashSet<Point> visited = new HashSet<Point>();

            void Visit(Point p, int dist)
            {
                visited.Add(p);

                // Traverse both parent and children, if not visited before.
                if (this[p] != Direction.None)
                {
                    var parent = Parent(p);
                    if (!visited.Contains(Parent(p)))
                    {
                        Visit(parent, dist + 1);
                        callback(parent, this[p].Flip(), dist + 1);
                    }
                }
                foreach (var d in ChildrenDirs(p))
                {
                    if (!visited.Contains(p.Add(d)))
                    {
                        Visit(p.Add(d), dist + 1);
                        callback(p.Add(d), d.Flip(), dist + 1);
                    }
                }
            }

            Visit(root, 0);
        }

        public int SubtreeSize(Point point)
        {
            if (!point.IsInRange(Width, Height))
                return 0;

            int count = 0;
            TraverseSubtree(point, (_, _2) => ++count);
            return count;
        }

        // Gets the number of neighboring gaps
        public int GetDensity(Point point)
        {
            return (from d in MapUtils.Directions
                    let p = point.Add(d)
                    where p.IsInRange(Width, Height) && IsRoom(p)
                    select p).Count();
        }

        // Returns a list of non-gap rooms.
        public List<Point> GetRooms()
        {
            return (from p in MapUtils.Range2D(Width, Height)
                    where IsRoom(p)
                    select p).ToList();
        }

        // Returns a list of specifically gaps.
        public List<Point> GetGaps()
        {
            return (from p in MapUtils.Range2D(Width, Height)
                    where this[p] == Direction.Gap
                    select p).ToList();
        }

        public List<Point> GetNonRooms()
        {
            return (from p in MapUtils.Range2D(Width, Height)
                    where !IsRoom(p)
                    select p).ToList();
        }

        // Returns the height of the tree underlying the map.
        public int GetTreeHeight()
        {
            var maxDepth = -1;

            TraverseSubtree(Base, (_, d) => maxDepth = Math.Max(maxDepth, d));

            return maxDepth;
        }

        /// <summary>
        /// Farthest point from p.
        /// </summary>
        public (Point, int) GetFarthestPoint(Point point)
        {
            var maxDist = 0;
            var farthest = point;

            TraverseWholeTree(point, (p, _, dist) =>
            {
                if (maxDist < dist)
                {
                    maxDist = dist;
                    farthest = p;
                }
            });

            return (farthest, maxDist);
        }

        /// <summary>
        /// Max distance from p to another point: https://en.wikipedia.org/wiki/Distance_(graph_theory).
        /// </summary>
        public int GetEccentricity(Point point) => GetFarthestPoint(point).Item2;

        public int GetDiameter() => GetEccentricity(GetFarthestPoint(Base).Item1);

        public void Clear()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    parentDirs[x, y] = Direction.None;
                }
            }

            CritEndpoints.Clear();
        }

        public SortedSet<Point> GetCritRooms()
        {
            var set = new SortedSet<Point>(new PointComparer());
            set.Add(Base);
            foreach (var e in CritEndpoints)
            {
                TraverseToBase(e, p => set.Add(p));
            }
            return set;
        }

        // Precondition: p must be a dead end.
        public void RemoveDeadEnd(Point p)
        {
            if (p == Base)
            {
                // Better have just one child. Replace base with its child.
                var dir = ChildrenDirs(Base).SingleOrDefault();
                if (dir != Direction.None)
                {
                    Base = Base.Add(dir);
                    this[Base] = Direction.None;
                    // Remove new base from crit endpoints
                    CritEndpoints.Remove(Base);
                }
            }
            else if (CritEndpoints.Contains(p))
            {
                CritEndpoints.Remove(p);
                AddCritEndpoint(Parent(p));
            }
            parentDirs[p.X, p.Y] = Direction.None;
        }

        // Sets the new base.
        public void Rebase(Point newBase)
        {
            if (newBase == Base)
                return;

            TraverseWholeTree(newBase, (p, dir, _) =>
            {
                this[p] = dir;
            });
            this[newBase] = Direction.None;
            Base = newBase;
        }

        public string ToPrettyString()
        {
            return parentDirs.ToPrettyString(a => DirectionToChar(a), string.Empty);
        }

        public override string ToString()
        {
            var sep = " ";
            var mapStr = parentDirs.ToPrettyString(a => DirectionToChar(a), string.Empty, "/");

            var critStr = string.Join(sep, from x in CritEndpoints where x != Boss select x.ToChessString());
            return $"{Width}{sep}{Height}{sep}{mapStr}{sep}{Base.ToChessString()}{sep}{Boss.ToChessString()}{sep}{critStr}";
        }

        private string DirectionToChar(Direction dir)
        {
            return dir == Direction.Gap ? "-" : dir == Direction.None ? "." : dir.ToString();
        }
    }
}
