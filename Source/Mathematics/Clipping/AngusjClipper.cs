﻿/*******************************************************************************
*                                                                              *
* Author    :  Angus Johnson                                                   *
* Version   :  6.2.1                                                           *
* Date      :  31 October 2014                                                 *
* Website   :  http://www.angusj.com                                           *
* Copyright :  Angus Johnson 2010-2014                                         *
*                                                                              *
* License:                                                                     *
* Use, modification & distribution is subject to Boost Software License Ver 1. *
* http://www.boost.org/LICENSE_1_0.txt                                         *
*                                                                              *
* Attributions:                                                                *
* The code in this library is an extension of Bala Vatti's clipping algorithm: *
* "A generic solution to polygon clipping"                                     *
* Communications of the ACM, Vol 35, Issue 7 (July 1992) pp 56-63.             *
* http://portal.acm.org/citation.cfm?id=129906                                 *
*                                                                              *
* Computer graphics and geometric modeling: implementation and algorithms      *
* By Max K. Agoston                                                            *
* Springer; 1 edition (January 4, 2005)                                        *
* http://books.google.com/books?q=vatti+clipping+agoston                       *
*                                                                              *
* See also:                                                                    *
* "Polygon Offsetting by Computing Winding Numbers"                            *
* Paper no. DETC2005-85513 pp. 565-575                                         *
* ASME 2005 International Design Engineering Technical Conferences             *
* and Computers and Information in Engineering Conference (IDETC/CIE2005)      *
* September 24-28, 2005 , Long Beach, California, USA                          *
* http://www.me.berkeley.edu/~mcmains/pubs/DAC05OffsetPolygon.pdf              *
*                                                                              *
*******************************************************************************/

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Penumbra.Mathematics.Clipping
{
    using Path = List<Vector2>;
    using Paths = List<List<Vector2>>;


    //------------------------------------------------------------------------------
    // PolyTree & PolyNode classes
    //------------------------------------------------------------------------------

    internal class PolyTree : PolyNode
    {
        internal List<PolyNode> m_AllPolys = new List<PolyNode>();

        ~PolyTree()
        {
            Clear();
        }

        public void Clear()
        {
            for (var i = 0; i < m_AllPolys.Count; i++)
                m_AllPolys[i] = null;
            m_AllPolys.Clear();
            m_Childs.Clear();
        }

        public PolyNode GetFirst()
        {
            if (m_Childs.Count > 0)
                return m_Childs[0];
            return null;
        }

        public int Total
        {
            get
            {
                var result = m_AllPolys.Count;
                //with negative offsets, ignore the hidden outer polygon ...
                if (result > 0 && m_Childs[0] != m_AllPolys[0]) result--;
                return result;
            }
        }
    }

    internal class PolyNode
    {
        internal PolyNode m_Parent;
        internal Path m_polygon = new Path();
        internal int m_Index;
        internal JoinType m_jointype;
        internal EndType m_endtype;
        internal List<PolyNode> m_Childs = new List<PolyNode>();

        private bool IsHoleNode()
        {
            var result = true;
            var node = m_Parent;
            while (node != null)
            {
                result = !result;
                node = node.m_Parent;
            }
            return result;
        }

        public int ChildCount => m_Childs.Count;

        public Path Contour => m_polygon;

        internal void AddChild(PolyNode Child)
        {
            var cnt = m_Childs.Count;
            m_Childs.Add(Child);
            Child.m_Parent = this;
            Child.m_Index = cnt;
        }

        public PolyNode GetNext()
        {
            return m_Childs.Count > 0 ? m_Childs[0] : GetNextSiblingUp();
        }

        internal PolyNode GetNextSiblingUp()
        {
            if (m_Parent == null)
                return null;
            return m_Index == m_Parent.m_Childs.Count - 1 
                ? m_Parent.GetNextSiblingUp() 
                : m_Parent.m_Childs[m_Index + 1];
        }

        public List<PolyNode> Childs => m_Childs;

        public PolyNode Parent => m_Parent;

        public bool IsHole => IsHoleNode();

        public bool IsOpen { get; set; }
    }


    //------------------------------------------------------------------------------
    //------------------------------------------------------------------------------

    internal enum ClipType
    {
        ctIntersection,
        ctUnion,
        ctDifference,
        ctXor
    };

    internal enum PolyType
    {
        ptSubject,
        ptClip
    };

    //By far the most widely used winding rules for polygon filling are
    //EvenOdd & NonZero (GDI, GDI+, XLib, OpenGL, Cairo, AGG, Quartz, SVG, Gr32)
    //Others rules include Positive, Negative and ABS_GTR_EQ_TWO (only in OpenGL)
    //see http://glprogramming.com/red/chapter11.html
    internal enum PolyFillType
    {
        pftEvenOdd,
        pftNonZero,
        pftPositive,
        pftNegative
    };

    internal enum JoinType
    {
        jtSquare,
        jtRound,
        jtMiter
    };

    internal enum EndType
    {
        etClosedPolygon,
        etClosedLine,
        etOpenButt,
        etOpenSquare,
        etOpenRound
    };

    internal enum EdgeSide
    {
        esLeft,
        esRight
    };

    internal enum Direction
    {
        dRightToLeft,
        dLeftToRight
    };

    internal class TEdge
    {
        internal Vector2 Bot;
        internal Vector2 Curr;
        internal Vector2 Top;
        internal Vector2 Delta;
        internal float Dx;
        internal PolyType PolyTyp;
        internal EdgeSide Side;
        internal int WindDelta; //1 or -1 depending on winding direction
        internal int WindCnt;
        internal int WindCnt2; //winding count of the opposite polytype
        internal int OutIdx;
        internal TEdge Next;
        internal TEdge Prev;
        internal TEdge NextInLML;
        internal TEdge NextInAEL;
        internal TEdge PrevInAEL;
        internal TEdge NextInSEL;
        internal TEdge PrevInSEL;
    };

    internal class IntersectNode
    {
        internal TEdge Edge1;
        internal TEdge Edge2;
        internal Vector2 Pt;
    };

    internal class MyIntersectNodeSort : IComparer<IntersectNode>
    {
        public int Compare(IntersectNode node1, IntersectNode node2)
        {
            var i = node2.Pt.Y - node1.Pt.Y;
            if (i > 0) return 1;
            if (i < 0) return -1;
            return 0;
        }
    }

    internal class LocalMinima
    {
        internal float Y;
        internal TEdge LeftBound;
        internal TEdge RightBound;
        internal LocalMinima Next;
    };

    internal class Scanbeam
    {
        internal float Y;
        internal Scanbeam Next;
    };

    internal class OutRec
    {
        internal int Idx;
        internal bool IsHole;
        internal bool IsOpen;
        internal OutRec FirstLeft; //see comments in clipper.pas
        internal OutPt Pts;
        internal OutPt BottomPt;
        internal PolyNode PolyNode;
    };

    internal class OutPt
    {
        internal int Idx;
        internal Vector2 Pt;
        internal OutPt Next;
        internal OutPt Prev;
    };

    internal class Join
    {
        internal OutPt OutPt1;
        internal OutPt OutPt2;
        internal Vector2 OffPt;
    };

    internal class ClipperBase
    {
        protected const float Horizontal = -3.4E+38f;
        protected const int Skip = -2;
        protected const int Unassigned = -1;
        protected const float Tolerance = 1.0E-20f;

        internal static bool NearZero(float val)
        {
            return (val > -Tolerance) && (val < Tolerance);
        }

        public const int LoRange = 0x7FFF;
        public const int HiRange = 0x7FFF;

        internal LocalMinima m_MinimaList;
        internal LocalMinima m_CurrentLM;
        internal List<List<TEdge>> m_edges = new List<List<TEdge>>();
        internal bool m_UseFullRange;
        internal bool m_HasOpenPaths;

        //------------------------------------------------------------------------------

        public bool PreserveCollinear { get; set; }
        //------------------------------------------------------------------------------

        public void Swap<T>(ref T val1, ref T val2) where T : struct
        {
            var tmp = val1;
            val1 = val2;
            val2 = tmp;
        }

        //------------------------------------------------------------------------------

        internal static bool IsHorizontal(TEdge e)
        {
            return Calc.NearZero(e.Delta.Y);
        }

        //------------------------------------------------------------------------------

        internal bool PointIsVertex(Vector2 pt, OutPt pp)
        {
            var pp2 = pp;
            do
            {
                if (pp2.Pt == pt) return true;
                pp2 = pp2.Next;
            } while (pp2 != pp);
            return false;
        }

        //------------------------------------------------------------------------------

        internal bool PointOnLineSegment(Vector2 pt,
            Vector2 linePt1, Vector2 linePt2, bool UseFullRange)
        {
            if (UseFullRange)
                return (Calc.NearEqual(pt.X, linePt1.X) && Calc.NearEqual(pt.Y, linePt1.Y)) ||
                       (Calc.NearEqual(pt.X, linePt2.X) && Calc.NearEqual(pt.Y, linePt2.Y)) ||
                       (((pt.X > linePt1.X) == (pt.X < linePt2.X)) &&
                        ((pt.Y > linePt1.Y) == (pt.Y < linePt2.Y)) &&
                        (Calc.NearEqual(((pt.X - linePt1.X)*(linePt2.Y - linePt1.Y)),
                          ((linePt2.X - linePt1.X)*(pt.Y - linePt1.Y)))));
            return (Calc.NearEqual(pt.X, linePt1.X) && Calc.NearEqual(pt.Y, linePt1.Y)) ||
                   (Calc.NearEqual(pt.X, linePt2.X) && Calc.NearEqual(pt.Y, linePt2.Y)) ||
                   (((pt.X > linePt1.X) == (pt.X < linePt2.X)) &&
                    ((pt.Y > linePt1.Y) == (pt.Y < linePt2.Y)) &&
                    (Calc.NearEqual((pt.X - linePt1.X)*(linePt2.Y - linePt1.Y),
                     (linePt2.X - linePt1.X)*(pt.Y - linePt1.Y))));
        }

        //------------------------------------------------------------------------------

        internal bool PointOnPolygon(Vector2 pt, OutPt pp, bool UseFullRange)
        {
            var pp2 = pp;
            while (true)
            {
                if (PointOnLineSegment(pt, pp2.Pt, pp2.Next.Pt, UseFullRange))
                    return true;
                pp2 = pp2.Next;
                if (pp2 == pp) break;
            }
            return false;
        }

        //------------------------------------------------------------------------------

        internal static bool SlopesEqual(TEdge e1, TEdge e2, bool UseFullRange)
        {
            if (UseFullRange)
                return Calc.NearEqual(e1.Delta.Y*e2.Delta.X,
                       e1.Delta.X*e2.Delta.Y);
            return Calc.NearEqual(e1.Delta.Y*e2.Delta.X,
                   e1.Delta.X*e2.Delta.Y);
        }

        //------------------------------------------------------------------------------

        protected static bool SlopesEqual(Vector2 pt1, Vector2 pt2,
            Vector2 pt3, bool UseFullRange)
        {
            if (UseFullRange)
                return Calc.NearEqual(pt1.Y - pt2.Y*pt2.X - pt3.X,
                       pt1.X - pt2.X*pt2.Y - pt3.Y);
            return Calc.NearEqual((pt1.Y - pt2.Y)*(pt2.X - pt3.X), (pt1.X - pt2.X)*(pt2.Y - pt3.Y));
        }

        //------------------------------------------------------------------------------

        protected static bool SlopesEqual(Vector2 pt1, Vector2 pt2,
            Vector2 pt3, Vector2 pt4, bool UseFullRange)
        {
            if (UseFullRange)
                return Calc.NearEqual((pt1.Y - pt2.Y*pt3.X - pt4.X),
                       (pt1.X - pt2.X*pt3.Y - pt4.Y));
            return
                Calc.NearEqual((pt1.Y - pt2.Y)*(pt3.X - pt4.X), (pt1.X - pt2.X)*(pt3.Y - pt4.Y));
        }

        //------------------------------------------------------------------------------

        internal ClipperBase() //constructor (nb: no external instantiation)
        {
            m_MinimaList = null;
            m_CurrentLM = null;
            m_UseFullRange = false;
            m_HasOpenPaths = false;
        }

        //------------------------------------------------------------------------------

        public virtual void Clear()
        {
            DisposeLocalMinimaList();
            for (var i = 0; i < m_edges.Count; ++i)
            {
                for (var j = 0; j < m_edges[i].Count; ++j) m_edges[i][j] = null;
                m_edges[i].Clear();
            }
            m_edges.Clear();
            m_UseFullRange = false;
            m_HasOpenPaths = false;
        }

        //------------------------------------------------------------------------------

        private void DisposeLocalMinimaList()
        {
            while (m_MinimaList != null)
            {
                var tmpLm = m_MinimaList.Next;
                m_MinimaList = null;
                m_MinimaList = tmpLm;
            }
            m_CurrentLM = null;
        }

        //------------------------------------------------------------------------------

        private static void RangeTest(Vector2 Pt, ref bool useFullRange)
        {
            while (true)
            {
                if (useFullRange)
                {
                    if (Pt.X > HiRange || Pt.Y > HiRange || -Pt.X > HiRange || -Pt.Y > HiRange)
                        throw new ClipperException("Coordinate outside allowed range");
                }
                else if (Pt.X > LoRange || Pt.Y > LoRange || -Pt.X > LoRange || -Pt.Y > LoRange)
                {
                    useFullRange = true;
                    continue;
                }
                break;
            }
        }

        //------------------------------------------------------------------------------

        private static void InitEdge(TEdge e, TEdge eNext,
            TEdge ePrev, Vector2 pt)
        {
            e.Next = eNext;
            e.Prev = ePrev;
            e.Curr = pt;
            e.OutIdx = Unassigned;
        }

        //------------------------------------------------------------------------------

        private static void InitEdge2(TEdge e, PolyType polyType)
        {
            if (e.Curr.Y >= e.Next.Curr.Y)
            {
                e.Bot = e.Curr;
                e.Top = e.Next.Curr;
            }
            else
            {
                e.Top = e.Curr;
                e.Bot = e.Next.Curr;
            }
            SetDx(e);
            e.PolyTyp = polyType;
        }

        //------------------------------------------------------------------------------

        private static TEdge FindNextLocMin(TEdge e)
        {
            for (;;)
            {
                while (e.Bot != e.Prev.Bot || e.Curr == e.Top) e = e.Next;
                if (!Calc.NearEqual(e.Dx, Horizontal )&& !Calc.NearEqual(e.Prev.Dx, Horizontal)) break;
                while (Calc.NearEqual(e.Prev.Dx, Horizontal)) e = e.Prev;
                TEdge E2 = e;
                while (Calc.NearEqual(e.Dx, Horizontal)) e = e.Next;
                if (Calc.NearEqual(e.Top.Y, e.Prev.Bot.Y)) continue; //ie just an intermediate horz.
                if (E2.Prev.Bot.X < e.Bot.X) e = E2;
                break;
            }
            return e;
        }

        //------------------------------------------------------------------------------

        private TEdge ProcessBound(TEdge e, bool leftBoundIsForward)
        {
            TEdge eStart, result = e;
            TEdge horz;

            if (result.OutIdx == Skip)
            {
                //check if there are edges beyond the skip edge in the bound and if so
                //create another LocMin and calling ProcessBound once more ...
                e = result;
                if (leftBoundIsForward)
                {
                    while (Calc.NearEqual(e.Top.Y, e.Next.Bot.Y)) e = e.Next;
                    while (e != result && Calc.NearEqual(e.Dx, Horizontal)) e = e.Prev;
                }
                else
                {
                    while (Calc.NearEqual(e.Top.Y, e.Prev.Bot.Y)) e = e.Prev;
                    while (e != result && Calc.NearEqual(e.Dx, Horizontal)) e = e.Next;
                }
                if (e == result)
                {
                    result = leftBoundIsForward ? e.Next : e.Prev;
                }
                else
                {
                    //there are more edges in the bound beyond result starting with E
                    e = leftBoundIsForward ? result.Next : result.Prev;
                    var locMin = new LocalMinima
                    {
                        Next = null,
                        Y = e.Bot.Y,
                        LeftBound = null,
                        RightBound = e
                    };
                    e.WindDelta = 0;
                    result = ProcessBound(e, leftBoundIsForward);
                    InsertLocalMinima(locMin);
                }
                return result;
            }

            if (Calc.NearEqual(e.Dx, Horizontal))
            {
                //We need to be careful with open paths because this may not be a
                //true local minima (ie E may be following a skip edge).
                //Also, consecutive horz. edges may start heading left before going right.
                eStart = leftBoundIsForward ? e.Prev : e.Next;
                if (eStart.OutIdx != Skip)
                {
                    if (Calc.NearEqual(eStart.Dx, Horizontal)) //ie an adjoining horizontal skip edge
                    {
                        if (!Calc.NearEqual(eStart.Bot.X, e.Bot.X) && !Calc.NearEqual(eStart.Top.X, e.Bot.X))
                            ReverseHorizontal(e);
                    }
                    else if (!Calc.NearEqual(eStart.Bot.X, e.Bot.X))
                        ReverseHorizontal(e);
                }
            }

            eStart = e;
            if (leftBoundIsForward)
            {
                while (Calc.NearEqual(result.Top.Y, result.Next.Bot.Y) && !Calc.NearEqual(result.Next.OutIdx, Skip))
                    result = result.Next;
                if (Calc.NearEqual(result.Dx, Horizontal) && result.Next.OutIdx != Skip)
                {
                    //nb: at the top of a bound, horizontals are added to the bound
                    //only when the preceding edge attaches to the horizontal's left vertex
                    //unless a Skip edge is encountered when that becomes the top divide
                    horz = result;
                    while (Calc.NearEqual(horz.Prev.Dx, Horizontal)) horz = horz.Prev;
                    if (Calc.NearEqual(horz.Prev.Top.X, result.Next.Top.X))
                    {
                    }
                    else if (horz.Prev.Top.X > result.Next.Top.X) result = horz.Prev;
                }
                while (e != result)
                {
                    e.NextInLML = e.Next;
                    if (Calc.NearEqual(e.Dx, Horizontal) && e != eStart && !Calc.NearEqual(e.Bot.X, e.Prev.Top.X))
                        ReverseHorizontal(e);
                    e = e.Next;
                }
                if (Calc.NearEqual(e.Dx, Horizontal) && e != eStart && !Calc.NearEqual(e.Bot.X, e.Prev.Top.X))
                    ReverseHorizontal(e);
                result = result.Next; //move to the edge just beyond current bound
            }
            else
            {
                while (Calc.NearEqual(result.Top.Y, result.Prev.Bot.Y) && result.Prev.OutIdx != Skip)
                    result = result.Prev;
                if (Calc.NearEqual(result.Dx, Horizontal) && result.Prev.OutIdx != Skip)
                {
                    horz = result;
                    while (Calc.NearEqual(horz.Next.Dx, Horizontal)) horz = horz.Next;
                    if (Calc.NearEqual(horz.Next.Top.X, result.Prev.Top.X))
                    {
                        result = horz.Next;
                    }
                    else if (horz.Next.Top.X > result.Prev.Top.X) result = horz.Next;
                }

                while (e != result)
                {
                    e.NextInLML = e.Prev;
                    if (Calc.NearEqual(e.Dx, Horizontal) && e != eStart && !Calc.NearEqual(e.Bot.X, e.Next.Top.X))
                        ReverseHorizontal(e);
                    e = e.Prev;
                }
                if (Calc.NearEqual(e.Dx, Horizontal) && e != eStart && !Calc.NearEqual(e.Bot.X, e.Next.Top.X))
                    ReverseHorizontal(e);
                result = result.Prev; //move to the edge just beyond current bound
            }
            return result;
        }

        //------------------------------------------------------------------------------


        public bool AddPath(Path pg, PolyType polyType, bool closed)
        {
            if (!closed)
                throw new ClipperException("AddPath: Open paths have been disabled.");

            var highI = pg.Count - 1;
            while (highI > 0 && (pg[highI] == pg[0])) --highI;
            while (highI > 0 && (pg[highI] == pg[highI - 1])) --highI;
            if ((highI < 2)) return false;

            //create a new edge array ...
            var edges = new List<TEdge>(highI + 1);
            for (var i = 0; i <= highI; i++) edges.Add(new TEdge());

            var IsFlat = true;

            //1. Basic (first) edge initialization ...
            edges[1].Curr = pg[1];
            RangeTest(pg[0], ref m_UseFullRange);
            RangeTest(pg[highI], ref m_UseFullRange);
            InitEdge(edges[0], edges[1], edges[highI], pg[0]);
            InitEdge(edges[highI], edges[0], edges[highI - 1], pg[highI]);
            for (var i = highI - 1; i >= 1; --i)
            {
                RangeTest(pg[i], ref m_UseFullRange);
                InitEdge(edges[i], edges[i + 1], edges[i - 1], pg[i]);
            }
            var eStart = edges[0];

            //2. Remove duplicate vertices, and (when closed) collinear edges ...
            TEdge E = eStart, eLoopStop = eStart;
            for (;;)
            {
                //nb: allows matching start and end points when not Closed ...
                if (E.Curr == E.Next.Curr)
                {
                    if (E == E.Next) break;
                    if (E == eStart) eStart = E.Next;
                    E = RemoveEdge(E);
                    eLoopStop = E;
                    continue;
                }
                if (E.Prev == E.Next)
                    break; //only two vertices
                if (SlopesEqual(E.Prev.Curr, E.Curr, E.Next.Curr, m_UseFullRange) &&
                    (!PreserveCollinear ||
                     !Pt2IsBetweenPt1AndPt3(E.Prev.Curr, E.Curr, E.Next.Curr)))
                {
                    //Collinear edges are allowed for open paths but in closed paths
                    //the default is to merge adjacent collinear edges into a single edge.
                    //However, if the PreserveCollinear property is enabled, only overlapping
                    //collinear edges (ie spikes) will be removed from closed paths.
                    if (E == eStart) eStart = E.Next;
                    E = RemoveEdge(E);
                    E = E.Prev;
                    eLoopStop = E;
                    continue;
                }
                E = E.Next;
                if ((E == eLoopStop)) break;
            }

            if (E.Prev == E.Next)
                return false;

            //3. Do second stage of edge initialization ...
            E = eStart;
            do
            {
                InitEdge2(E, polyType);
                E = E.Next;
                if (IsFlat && !Calc.NearEqual(E.Curr.Y, eStart.Curr.Y)) IsFlat = false;
            } while (E != eStart);

            //4. Finally, add edge bounds to LocalMinima list ...

            //Totally flat paths must be handled differently when adding them
            //to LocalMinima list to avoid endless loops etc ...
            if (IsFlat)
            {
                return false;
            }

            m_edges.Add(edges);
            TEdge EMin = null;

            //workaround to avoid an endless loop in the while loop below when
            //open paths have matching start and end points ...
            if (E.Prev.Bot == E.Prev.Top) E = E.Next;

            for (;;)
            {
                E = FindNextLocMin(E);
                if (E == EMin) break;
                if (EMin == null) EMin = E;

                //E and E.Prev now share a local minima (left aligned if horizontal).
                //Compare their slopes to find which starts which bound ...
                var locMin = new LocalMinima
                {
                    Next = null,
                    Y = E.Bot.Y
                };
                bool leftBoundIsForward;
                if (E.Dx < E.Prev.Dx)
                {
                    locMin.LeftBound = E.Prev;
                    locMin.RightBound = E;
                    leftBoundIsForward = false; //Q.nextInLML = Q.prev
                }
                else
                {
                    locMin.LeftBound = E;
                    locMin.RightBound = E.Prev;
                    leftBoundIsForward = true; //Q.nextInLML = Q.next
                }
                locMin.LeftBound.Side = EdgeSide.esLeft;
                locMin.RightBound.Side = EdgeSide.esRight;

                if (locMin.LeftBound.Next == locMin.RightBound)
                    locMin.LeftBound.WindDelta = -1;
                else locMin.LeftBound.WindDelta = 1;
                locMin.RightBound.WindDelta = -locMin.LeftBound.WindDelta;

                E = ProcessBound(locMin.LeftBound, leftBoundIsForward);
                if (E.OutIdx == Skip) E = ProcessBound(E, leftBoundIsForward);

                var E2 = ProcessBound(locMin.RightBound, !leftBoundIsForward);
                if (E2.OutIdx == Skip) E2 = ProcessBound(E2, !leftBoundIsForward);

                if (locMin.LeftBound.OutIdx == Skip)
                    locMin.LeftBound = null;
                else if (locMin.RightBound.OutIdx == Skip)
                    locMin.RightBound = null;
                InsertLocalMinima(locMin);
                if (!leftBoundIsForward) E = E2;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        public bool AddPaths(Paths ppg, PolyType polyType, bool closed)
        {
            var result = false;
            for (var i = 0; i < ppg.Count; ++i)
                if (AddPath(ppg[i], polyType, closed)) result = true;
            return result;
        }

        //------------------------------------------------------------------------------

        internal bool Pt2IsBetweenPt1AndPt3(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        {
            if ((pt1 == pt3) || (pt1 == pt2) || (pt3 == pt2)) return false;
            if (!Calc.NearEqual(pt1.X, pt3.X)) return (pt2.X > pt1.X) == (pt2.X < pt3.X);
            return (pt2.Y > pt1.Y) == (pt2.Y < pt3.Y);
        }

        //------------------------------------------------------------------------------

        private static TEdge RemoveEdge(TEdge e)
        {
            //removes e from float_linked_list (but without removing from memory)
            e.Prev.Next = e.Next;
            e.Next.Prev = e.Prev;
            var result = e.Next;
            e.Prev = null; //flag as removed (see ClipperBase.Clear)
            return result;
        }

        //------------------------------------------------------------------------------

        private static void SetDx(TEdge e)
        {
            e.Delta.X = (e.Top.X - e.Bot.X);
            e.Delta.Y = (e.Top.Y - e.Bot.Y);
            if (Calc.NearZero(e.Delta.Y)) e.Dx = Horizontal;
            else e.Dx = e.Delta.X/(e.Delta.Y);
        }

        //---------------------------------------------------------------------------

        private void InsertLocalMinima(LocalMinima newLm)
        {
            if (m_MinimaList == null)
            {
                m_MinimaList = newLm;
            }
            else if (newLm.Y >= m_MinimaList.Y)
            {
                newLm.Next = m_MinimaList;
                m_MinimaList = newLm;
            }
            else
            {
                var tmpLm = m_MinimaList;
                while (tmpLm.Next != null && (newLm.Y < tmpLm.Next.Y))
                    tmpLm = tmpLm.Next;
                newLm.Next = tmpLm.Next;
                tmpLm.Next = newLm;
            }
        }

        //------------------------------------------------------------------------------

        protected void PopLocalMinima()
        {
            m_CurrentLM = m_CurrentLM?.Next;
        }

        //------------------------------------------------------------------------------

        private void ReverseHorizontal(TEdge e)
        {
            //swap horizontal edges' top and bottom x's so they follow the natural
            //progression of the bounds - ie so their xbots will align with the
            //adjoining lower edge. [Helpful in the ProcessHorizontal() method.]
            Swap(ref e.Top.X, ref e.Bot.X);
        }

        //------------------------------------------------------------------------------

        protected virtual void Reset()
        {
            m_CurrentLM = m_MinimaList;
            if (m_CurrentLM == null) return; //ie nothing to process

            //reset all edges ...
            var lm = m_MinimaList;
            while (lm != null)
            {
                var e = lm.LeftBound;
                if (e != null)
                {
                    e.Curr = e.Bot;
                    e.Side = EdgeSide.esLeft;
                    e.OutIdx = Unassigned;
                }
                e = lm.RightBound;
                if (e != null)
                {
                    e.Curr = e.Bot;
                    e.Side = EdgeSide.esRight;
                    e.OutIdx = Unassigned;
                }
                lm = lm.Next;
            }
        }

        //------------------------------------------------------------------------------

        public static RectangleF GetBounds(Paths paths)
        {
            int i = 0, cnt = paths.Count;
            while (i < cnt && paths[i].Count == 0) i++;
            if (i == cnt) return new RectangleF(0, 0, 0, 0);
            var result = new RectangleF(
                paths[i][0].X,
                paths[i][0].Y,
                0,
                0);
            for (; i < cnt; i++)
                for (var j = 0; j < paths[i].Count; j++)
                {
                    if (paths[i][j].X < result.Left)
                    {
                        //result.Left = paths[i][j].X;
                        result = new RectangleF(paths[i][j].X, result.Top, result.Right - paths[i][j].X,
                            result.Height);
                    }
                    else if (paths[i][j].X > result.Right)
                    {
                        //result.Right = paths[i][j].X;
                        result = new RectangleF(result.Left, result.Top, paths[i][j].X - result.Left,
                            result.Height);
                    }
                    if (paths[i][j].Y < result.Top)
                    {
                        //result.Top = paths[i][j].Y;
                        result = new RectangleF(result.Left, paths[i][j].X, result.Width,
                            result.Bottom - paths[i][j].Y);
                    }
                    else if (paths[i][j].Y > result.Bottom)
                    {
                        //result.Bottom = paths[i][j].Y;
                        result = new RectangleF(result.Left, result.Top, result.Width, paths[i][j].Y - result.Top);
                    }
                }
            return result;
        }
    } //end ClipperBase

    internal class AngusjClipper : ClipperBase
    {
        //InitOptions that can be passed to the constructor ...
        public const int ioReverseSolution = 1;
        public const int ioStrictlySimple = 2;
        public const int ioPreserveCollinear = 4;

        private readonly List<OutRec> m_PolyOuts;
        private ClipType m_ClipType;
        private Scanbeam m_Scanbeam;
        private TEdge m_ActiveEdges;
        private TEdge m_SortedEdges;
        private readonly List<IntersectNode> m_IntersectList;
        private readonly IComparer<IntersectNode> m_IntersectNodeComparer;
        private bool m_ExecuteLocked;
        private PolyFillType m_ClipFillType;
        private PolyFillType m_SubjFillType;
        private readonly List<Join> m_Joins;
        private readonly List<Join> m_GhostJoins;
        private bool m_UsingPolyTree;

        public AngusjClipper(int InitOptions = 0) //constructor
        {
            m_Scanbeam = null;
            m_ActiveEdges = null;
            m_SortedEdges = null;
            m_IntersectList = new List<IntersectNode>();
            m_IntersectNodeComparer = new MyIntersectNodeSort();
            m_ExecuteLocked = false;
            m_UsingPolyTree = false;
            m_PolyOuts = new List<OutRec>();
            m_Joins = new List<Join>();
            m_GhostJoins = new List<Join>();
            ReverseSolution = (ioReverseSolution & InitOptions) != 0;
            StrictlySimple = (ioStrictlySimple & InitOptions) != 0;
            PreserveCollinear = (ioPreserveCollinear & InitOptions) != 0;
        }

        //------------------------------------------------------------------------------

        protected override void Reset()
        {
            base.Reset();
            m_Scanbeam = null;
            m_ActiveEdges = null;
            m_SortedEdges = null;
            var lm = m_MinimaList;
            while (lm != null)
            {
                InsertScanbeam(lm.Y);
                lm = lm.Next;
            }
        }

        //------------------------------------------------------------------------------

        public bool ReverseSolution { get; set; }
        //------------------------------------------------------------------------------

        public bool StrictlySimple { get; set; }
        //------------------------------------------------------------------------------

        private void InsertScanbeam(float y)
        {
            if (m_Scanbeam == null)
            {
                m_Scanbeam = new Scanbeam
                {
                    Next = null,
                    Y = y
                };
            }
            else if (y > m_Scanbeam.Y)
            {
                var newSb = new Scanbeam
                {
                    Y = y,
                    Next = m_Scanbeam
                };
                m_Scanbeam = newSb;
            }
            else
            {
                var sb2 = m_Scanbeam;
                while (sb2.Next != null && (y <= sb2.Next.Y)) sb2 = sb2.Next;
                if (Calc.NearEqual(y, sb2.Y)) return; //ie ignores duplicates
                var newSb = new Scanbeam
                {
                    Y = y,
                    Next = sb2.Next
                };
                sb2.Next = newSb;
            }
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, Paths solution,
            PolyFillType subjFillType, PolyFillType clipFillType)
        {
            if (m_ExecuteLocked) return false;
            if (m_HasOpenPaths)
                throw
                    new ClipperException("Error: PolyTree struct is need for open path clipping.");

            m_ExecuteLocked = true;
            solution.Clear();
            m_SubjFillType = subjFillType;
            m_ClipFillType = clipFillType;
            m_ClipType = clipType;
            m_UsingPolyTree = false;
            bool succeeded;
            try
            {
                succeeded = ExecuteInternal();
                //build the return polygons ...
                if (succeeded) BuildResult(solution);
            }
            finally
            {
                DisposeAllPolyPts();
                m_ExecuteLocked = false;
            }
            return succeeded;
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, PolyTree polytree,
            PolyFillType subjFillType, PolyFillType clipFillType)
        {
            if (m_ExecuteLocked) return false;
            m_ExecuteLocked = true;
            m_SubjFillType = subjFillType;
            m_ClipFillType = clipFillType;
            m_ClipType = clipType;
            m_UsingPolyTree = true;
            bool succeeded;
            try
            {
                succeeded = ExecuteInternal();
                //build the return polygons ...
                if (succeeded) BuildResult2(polytree);
            }
            finally
            {
                DisposeAllPolyPts();
                m_ExecuteLocked = false;
            }
            return succeeded;
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, Paths solution)
        {
            return Execute(clipType, solution,
                PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, PolyTree polytree)
        {
            return Execute(clipType, polytree,
                PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);
        }

        //------------------------------------------------------------------------------

        internal void FixHoleLinkage(OutRec outRec)
        {
            //skip if an outermost polygon or
            //already already points to the correct FirstLeft ...
            if (outRec.FirstLeft == null ||
                (outRec.IsHole != outRec.FirstLeft.IsHole &&
                 outRec.FirstLeft.Pts != null)) return;

            var orfl = outRec.FirstLeft;
            while (orfl != null && ((orfl.IsHole == outRec.IsHole) || orfl.Pts == null))
                orfl = orfl.FirstLeft;
            outRec.FirstLeft = orfl;
        }

        //------------------------------------------------------------------------------

        private bool ExecuteInternal()
        {
            try
            {
                Reset();
                if (m_CurrentLM == null) return false;

                var botY = PopScanbeam();
                do
                {
                    InsertLocalMinimaIntoAEL(botY);
                    m_GhostJoins.Clear();
                    ProcessHorizontals(false);
                    if (m_Scanbeam == null) break;
                    var topY = PopScanbeam();
                    if (!ProcessIntersections(topY)) return false;
                    ProcessEdgesAtTopOfScanbeam(topY);
                    botY = topY;
                } while (m_Scanbeam != null || m_CurrentLM != null);

                //fix orientations ...
                for (var i = 0; i < m_PolyOuts.Count; i++)
                {
                    var outRec = m_PolyOuts[i];
                    if (outRec.Pts == null || outRec.IsOpen) continue;
                    if ((outRec.IsHole ^ ReverseSolution) == (Area(outRec) > 0))
                        ReversePolyPtLinks(outRec.Pts);
                }

                JoinCommonEdges();

                for (var i = 0; i < m_PolyOuts.Count; i++)
                {
                    var outRec = m_PolyOuts[i];
                    if (outRec.Pts != null && !outRec.IsOpen)
                        FixupOutPolygon(outRec);
                }

                if (StrictlySimple) DoSimplePolygons();
                return true;
            }                
            finally
            {
                m_Joins.Clear();
                m_GhostJoins.Clear();
            }
        }

        //------------------------------------------------------------------------------

        private float PopScanbeam()
        {
            var Y = m_Scanbeam.Y;
            m_Scanbeam = m_Scanbeam.Next;
            return Y;
        }

        //------------------------------------------------------------------------------

        private void DisposeAllPolyPts()
        {
            for (var i = 0; i < m_PolyOuts.Count; ++i) DisposeOutRec(i);
            m_PolyOuts.Clear();
        }

        //------------------------------------------------------------------------------

        private void DisposeOutRec(int index)
        {
            OutRec outRec = m_PolyOuts[index];
            outRec.Pts = null;            
            m_PolyOuts[index] = null;
        }

        //------------------------------------------------------------------------------

        private void AddJoin(OutPt Op1, OutPt Op2, Vector2 OffPt)
        {
            var j = new Join
            {
                OutPt1 = Op1,
                OutPt2 = Op2,
                OffPt = OffPt
            };
            m_Joins.Add(j);
        }

        //------------------------------------------------------------------------------

        private void AddGhostJoin(OutPt Op, Vector2 OffPt)
        {
            var j = new Join
            {
                OutPt1 = Op,
                OffPt = OffPt
            };
            m_GhostJoins.Add(j);
        }

        //------------------------------------------------------------------------------

        private void InsertLocalMinimaIntoAEL(float botY)
        {
            while (m_CurrentLM != null && Calc.NearEqual(m_CurrentLM.Y, botY))
            {
                var lb = m_CurrentLM.LeftBound;
                var rb = m_CurrentLM.RightBound;
                PopLocalMinima();

                OutPt Op1 = null;
                if (lb == null)
                {
                    InsertEdgeIntoAEL(rb, null);
                    SetWindingCount(rb);
                    if (IsContributing(rb))
                        Op1 = AddOutPt(rb, rb.Bot);
                }
                else if (rb == null)
                {
                    InsertEdgeIntoAEL(lb, null);
                    SetWindingCount(lb);
                    if (IsContributing(lb))
                        Op1 = AddOutPt(lb, lb.Bot);
                    InsertScanbeam(lb.Top.Y);
                }
                else
                {
                    InsertEdgeIntoAEL(lb, null);
                    InsertEdgeIntoAEL(rb, lb);
                    SetWindingCount(lb);
                    rb.WindCnt = lb.WindCnt;
                    rb.WindCnt2 = lb.WindCnt2;
                    if (IsContributing(lb))
                        Op1 = AddLocalMinPoly(lb, rb, lb.Bot);
                    InsertScanbeam(lb.Top.Y);
                }

                if (rb != null)
                {
                    if (IsHorizontal(rb))
                        AddEdgeToSEL(rb);
                    else
                        InsertScanbeam(rb.Top.Y);
                }

                if (lb == null || rb == null) continue;

                //if output polygons share an Edge with a horizontal rb, they'll need joining later ...
                if (Op1 != null && IsHorizontal(rb) &&
                    m_GhostJoins.Count > 0 && rb.WindDelta != 0)
                {
                    for (var i = 0; i < m_GhostJoins.Count; i++)
                    {
                        //if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        //the 'ghost' join to a real join ready for later ...
                        var j = m_GhostJoins[i];
                        if (HorzSegmentsOverlap(j.OutPt1.Pt.X, j.OffPt.X, rb.Bot.X, rb.Top.X))
                            AddJoin(j.OutPt1, Op1, j.OffPt);
                    }
                }

                if (lb.OutIdx >= 0 && lb.PrevInAEL != null &&
                    Calc.NearEqual(lb.PrevInAEL.Curr.X, lb.Bot.X) &&
                    lb.PrevInAEL.OutIdx >= 0 &&
                    SlopesEqual(lb.PrevInAEL, lb, m_UseFullRange) &&
                    lb.WindDelta != 0 && lb.PrevInAEL.WindDelta != 0)
                {
                    var Op2 = AddOutPt(lb.PrevInAEL, lb.Bot);
                    AddJoin(Op1, Op2, lb.Top);
                }

                if (lb.NextInAEL != rb)
                {
                    if (rb.OutIdx >= 0 && rb.PrevInAEL.OutIdx >= 0 &&
                        SlopesEqual(rb.PrevInAEL, rb, m_UseFullRange) &&
                        rb.WindDelta != 0 && rb.PrevInAEL.WindDelta != 0)
                    {
                        var Op2 = AddOutPt(rb.PrevInAEL, rb.Bot);
                        AddJoin(Op1, Op2, rb.Top);
                    }

                    var e = lb.NextInAEL;
                    if (e != null)
                        while (e != rb)
                        {
                            //nb: For calculating winding counts etc, IntersectEdges() assumes
                            //that param1 will be to the right of param2 ABOVE the intersection ...
                            IntersectEdges(rb, e, lb.Curr); //order important here
                            e = e.NextInAEL;
                        }
                }
            }
        }

        //------------------------------------------------------------------------------

        private void InsertEdgeIntoAEL(TEdge edge, TEdge startEdge)
        {
            if (m_ActiveEdges == null)
            {
                edge.PrevInAEL = null;
                edge.NextInAEL = null;
                m_ActiveEdges = edge;
            }
            else if (startEdge == null && E2InsertsBeforeE1(m_ActiveEdges, edge))
            {
                edge.PrevInAEL = null;
                edge.NextInAEL = m_ActiveEdges;
                m_ActiveEdges.PrevInAEL = edge;
                m_ActiveEdges = edge;
            }
            else
            {
                if (startEdge == null) startEdge = m_ActiveEdges;
                while (startEdge.NextInAEL != null &&
                       !E2InsertsBeforeE1(startEdge.NextInAEL, edge))
                    startEdge = startEdge.NextInAEL;
                edge.NextInAEL = startEdge.NextInAEL;
                if (startEdge.NextInAEL != null) startEdge.NextInAEL.PrevInAEL = edge;
                edge.PrevInAEL = startEdge;
                startEdge.NextInAEL = edge;
            }
        }

        //----------------------------------------------------------------------

        private static bool E2InsertsBeforeE1(TEdge e1, TEdge e2)
        {
            if (Calc.NearEqual(e2.Curr.X, e1.Curr.X))
            {
                if (e2.Top.Y > e1.Top.Y)
                    return e2.Top.X < TopX(e1, e2.Top.Y);
                return e1.Top.X > TopX(e2, e1.Top.Y);
            }
            return e2.Curr.X < e1.Curr.X;
        }

        //------------------------------------------------------------------------------

        private bool IsEvenOddFillType(TEdge edge)
        {
            if (edge.PolyTyp == PolyType.ptSubject)
                return m_SubjFillType == PolyFillType.pftEvenOdd;
            return m_ClipFillType == PolyFillType.pftEvenOdd;
        }

        //------------------------------------------------------------------------------

        private bool IsEvenOddAltFillType(TEdge edge)
        {
            if (edge.PolyTyp == PolyType.ptSubject)
                return m_ClipFillType == PolyFillType.pftEvenOdd;
            return m_SubjFillType == PolyFillType.pftEvenOdd;
        }

        //------------------------------------------------------------------------------

        private bool IsContributing(TEdge edge)
        {
            PolyFillType pft, pft2;
            if (edge.PolyTyp == PolyType.ptSubject)
            {
                pft = m_SubjFillType;
                pft2 = m_ClipFillType;
            }
            else
            {
                pft = m_ClipFillType;
                pft2 = m_SubjFillType;
            }

            switch (pft)
            {
                case PolyFillType.pftEvenOdd:
                    //return false if a subj line has been flagged as inside a subj polygon
                    if (edge.WindDelta == 0 && edge.WindCnt != 1) return false;
                    break;
                case PolyFillType.pftNonZero:
                    if (Math.Abs(edge.WindCnt) != 1) return false;
                    break;
                case PolyFillType.pftPositive:
                    if (edge.WindCnt != 1) return false;
                    break;
                default: //PolyFillType.pftNegative
                    if (edge.WindCnt != -1) return false;
                    break;
            }

            switch (m_ClipType)
            {
                case ClipType.ctIntersection:
                    switch (pft2)
                    {
                        case PolyFillType.pftEvenOdd:
                        case PolyFillType.pftNonZero:
                            return (edge.WindCnt2 != 0);
                        case PolyFillType.pftPositive:
                            return (edge.WindCnt2 > 0);
                        default:
                            return (edge.WindCnt2 < 0);
                    }
                case ClipType.ctUnion:
                    switch (pft2)
                    {
                        case PolyFillType.pftEvenOdd:
                        case PolyFillType.pftNonZero:
                            return (edge.WindCnt2 == 0);
                        case PolyFillType.pftPositive:
                            return (edge.WindCnt2 <= 0);
                        default:
                            return (edge.WindCnt2 >= 0);
                    }
                case ClipType.ctDifference:
                    if (edge.PolyTyp == PolyType.ptSubject)
                        switch (pft2)
                        {
                            case PolyFillType.pftEvenOdd:
                            case PolyFillType.pftNonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.pftPositive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    switch (pft2)
                    {
                        case PolyFillType.pftEvenOdd:
                        case PolyFillType.pftNonZero:
                            return (edge.WindCnt2 != 0);
                        case PolyFillType.pftPositive:
                            return (edge.WindCnt2 > 0);
                        default:
                            return (edge.WindCnt2 < 0);
                    }
                case ClipType.ctXor:
                    if (edge.WindDelta == 0) //XOr always contributing unless open
                        switch (pft2)
                        {
                            case PolyFillType.pftEvenOdd:
                            case PolyFillType.pftNonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.pftPositive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    return true;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private void SetWindingCount(TEdge edge)
        {
            var e = edge.PrevInAEL;
            //find the edge of the same polytype that immediately preceeds 'edge' in AEL
            while (e != null && ((e.PolyTyp != edge.PolyTyp) || (e.WindDelta == 0))) e = e.PrevInAEL;
            if (e == null)
            {
                edge.WindCnt = (edge.WindDelta == 0 ? 1 : edge.WindDelta);
                edge.WindCnt2 = 0;
                e = m_ActiveEdges; //ie get ready to calc WindCnt2
            }
            else if (edge.WindDelta == 0 && m_ClipType != ClipType.ctUnion)
            {
                edge.WindCnt = 1;
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }
            else if (IsEvenOddFillType(edge))
            {
                //EvenOdd filling ...
                if (edge.WindDelta == 0)
                {
                    //are we inside a subj polygon ...
                    var Inside = true;
                    var e2 = e.PrevInAEL;
                    while (e2 != null)
                    {
                        if (e2.PolyTyp == e.PolyTyp && e2.WindDelta != 0)
                            Inside = !Inside;
                        e2 = e2.PrevInAEL;
                    }
                    edge.WindCnt = (Inside ? 0 : 1);
                }
                else
                {
                    edge.WindCnt = edge.WindDelta;
                }
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                if (e.WindCnt*e.WindDelta < 0)
                {
                    //prev edge is 'decreasing' WindCount (WC) toward zero
                    //so we're outside the previous polygon ...
                    if (Math.Abs(e.WindCnt) > 1)
                    {
                        //outside prev poly but still inside another.
                        //when reversing direction of prev poly use the same WC 
                        if (e.WindDelta*edge.WindDelta < 0) edge.WindCnt = e.WindCnt;
                        //otherwise continue to 'decrease' WC ...
                        else edge.WindCnt = e.WindCnt + edge.WindDelta;
                    }
                    else
                    //now outside all polys of same polytype so set own WC ...
                        edge.WindCnt = (edge.WindDelta == 0 ? 1 : edge.WindDelta);
                }
                else
                {
                    //prev edge is 'increasing' WindCount (WC) away from zero
                    //so we're inside the previous polygon ...
                    if (edge.WindDelta == 0)
                        edge.WindCnt = (e.WindCnt < 0 ? e.WindCnt - 1 : e.WindCnt + 1);
                    //if wind direction is reversing prev then use same WC
                    else if (e.WindDelta*edge.WindDelta < 0)
                        edge.WindCnt = e.WindCnt;
                    //otherwise add to WC ...
                    else edge.WindCnt = e.WindCnt + edge.WindDelta;
                }
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }

            //update WindCnt2 ...
            if (IsEvenOddAltFillType(edge))
            {
                //EvenOdd filling ...
                while (e != edge)
                {
                    if (e.WindDelta != 0)
                        edge.WindCnt2 = (edge.WindCnt2 == 0 ? 1 : 0);
                    e = e.NextInAEL;
                }
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                while (e != edge)
                {
                    edge.WindCnt2 += e.WindDelta;
                    e = e.NextInAEL;
                }
            }
        }

        //------------------------------------------------------------------------------

        private void AddEdgeToSEL(TEdge edge)
        {
            //SEL pointers in PEdge are reused to build a list of horizontal edges.
            //However, we don't need to worry about order with horizontal edge processing.
            if (m_SortedEdges == null)
            {
                m_SortedEdges = edge;
                edge.PrevInSEL = null;
                edge.NextInSEL = null;
            }
            else
            {
                edge.NextInSEL = m_SortedEdges;
                edge.PrevInSEL = null;
                m_SortedEdges.PrevInSEL = edge;
                m_SortedEdges = edge;
            }
        }

        //------------------------------------------------------------------------------

        private void CopyAELToSEL()
        {
            var e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e != null)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e = e.NextInAEL;
            }
        }

        //------------------------------------------------------------------------------

        private void SwapPositionsInAEL(TEdge edge1, TEdge edge2)
        {
            //check that one or other edge hasn't already been removed from AEL ...
            if (edge1.NextInAEL == edge1.PrevInAEL ||
                edge2.NextInAEL == edge2.PrevInAEL) return;

            if (edge1.NextInAEL == edge2)
            {
                var next = edge2.NextInAEL;
                if (next != null)
                    next.PrevInAEL = edge1;
                var prev = edge1.PrevInAEL;
                if (prev != null)
                    prev.NextInAEL = edge2;
                edge2.PrevInAEL = prev;
                edge2.NextInAEL = edge1;
                edge1.PrevInAEL = edge2;
                edge1.NextInAEL = next;
            }
            else if (edge2.NextInAEL == edge1)
            {
                var next = edge1.NextInAEL;
                if (next != null)
                    next.PrevInAEL = edge2;
                var prev = edge2.PrevInAEL;
                if (prev != null)
                    prev.NextInAEL = edge1;
                edge1.PrevInAEL = prev;
                edge1.NextInAEL = edge2;
                edge2.PrevInAEL = edge1;
                edge2.NextInAEL = next;
            }
            else
            {
                var next = edge1.NextInAEL;
                var prev = edge1.PrevInAEL;
                edge1.NextInAEL = edge2.NextInAEL;
                if (edge1.NextInAEL != null)
                    edge1.NextInAEL.PrevInAEL = edge1;
                edge1.PrevInAEL = edge2.PrevInAEL;
                if (edge1.PrevInAEL != null)
                    edge1.PrevInAEL.NextInAEL = edge1;
                edge2.NextInAEL = next;
                if (edge2.NextInAEL != null)
                    edge2.NextInAEL.PrevInAEL = edge2;
                edge2.PrevInAEL = prev;
                if (edge2.PrevInAEL != null)
                    edge2.PrevInAEL.NextInAEL = edge2;
            }

            if (edge1.PrevInAEL == null)
                m_ActiveEdges = edge1;
            else if (edge2.PrevInAEL == null)
                m_ActiveEdges = edge2;
        }

        //------------------------------------------------------------------------------

        private void SwapPositionsInSEL(TEdge edge1, TEdge edge2)
        {
            if (edge1.NextInSEL == null && edge1.PrevInSEL == null)
                return;
            if (edge2.NextInSEL == null && edge2.PrevInSEL == null)
                return;

            if (edge1.NextInSEL == edge2)
            {
                var next = edge2.NextInSEL;
                if (next != null)
                    next.PrevInSEL = edge1;
                var prev = edge1.PrevInSEL;
                if (prev != null)
                    prev.NextInSEL = edge2;
                edge2.PrevInSEL = prev;
                edge2.NextInSEL = edge1;
                edge1.PrevInSEL = edge2;
                edge1.NextInSEL = next;
            }
            else if (edge2.NextInSEL == edge1)
            {
                var next = edge1.NextInSEL;
                if (next != null)
                    next.PrevInSEL = edge2;
                var prev = edge2.PrevInSEL;
                if (prev != null)
                    prev.NextInSEL = edge1;
                edge1.PrevInSEL = prev;
                edge1.NextInSEL = edge2;
                edge2.PrevInSEL = edge1;
                edge2.NextInSEL = next;
            }
            else
            {
                var next = edge1.NextInSEL;
                var prev = edge1.PrevInSEL;
                edge1.NextInSEL = edge2.NextInSEL;
                if (edge1.NextInSEL != null)
                    edge1.NextInSEL.PrevInSEL = edge1;
                edge1.PrevInSEL = edge2.PrevInSEL;
                if (edge1.PrevInSEL != null)
                    edge1.PrevInSEL.NextInSEL = edge1;
                edge2.NextInSEL = next;
                if (edge2.NextInSEL != null)
                    edge2.NextInSEL.PrevInSEL = edge2;
                edge2.PrevInSEL = prev;
                if (edge2.PrevInSEL != null)
                    edge2.PrevInSEL.NextInSEL = edge2;
            }

            if (edge1.PrevInSEL == null)
                m_SortedEdges = edge1;
            else if (edge2.PrevInSEL == null)
                m_SortedEdges = edge2;
        }

        //------------------------------------------------------------------------------


        private void AddLocalMaxPoly(TEdge e1, TEdge e2, Vector2 pt)
        {
            AddOutPt(e1, pt);
            if (e2.WindDelta == 0) AddOutPt(e2, pt);
            if (e1.OutIdx == e2.OutIdx)
            {
                e1.OutIdx = Unassigned;
                e2.OutIdx = Unassigned;
            }
            else if (e1.OutIdx < e2.OutIdx)
                AppendPolygon(e1, e2);
            else
                AppendPolygon(e2, e1);
        }

        //------------------------------------------------------------------------------

        private OutPt AddLocalMinPoly(TEdge e1, TEdge e2, Vector2 pt)
        {
            OutPt result;
            TEdge e, prevE;
            if (IsHorizontal(e2) || (e1.Dx > e2.Dx))
            {
                result = AddOutPt(e1, pt);
                e2.OutIdx = e1.OutIdx;
                e1.Side = EdgeSide.esLeft;
                e2.Side = EdgeSide.esRight;
                e = e1;
                prevE = e.PrevInAEL == e2 ? e2.PrevInAEL : e.PrevInAEL;
            }
            else
            {
                result = AddOutPt(e2, pt);
                e1.OutIdx = e2.OutIdx;
                e1.Side = EdgeSide.esRight;
                e2.Side = EdgeSide.esLeft;
                e = e2;
                prevE = e.PrevInAEL == e1 ? e1.PrevInAEL : e.PrevInAEL;
            }

            if (prevE != null && prevE.OutIdx >= 0 &&
               Calc.NearEqual(TopX(prevE, pt.Y), TopX(e, pt.Y)) &&
                SlopesEqual(e, prevE, m_UseFullRange) &&
                (e.WindDelta != 0) && (prevE.WindDelta != 0))
            {
                var outPt = AddOutPt(prevE, pt);
                AddJoin(result, outPt, e.Top);
            }
            return result;
        }

        //------------------------------------------------------------------------------

        private OutRec CreateOutRec()
        {
            var result = new OutRec
            {
                Idx = Unassigned,
                IsHole = false,
                IsOpen = false,
                FirstLeft = null,
                Pts = null,
                BottomPt = null,
                PolyNode = null
            };
            m_PolyOuts.Add(result);
            result.Idx = m_PolyOuts.Count - 1;
            return result;
        }

        //------------------------------------------------------------------------------

        private OutPt AddOutPt(TEdge e, Vector2 pt)
        {
            var ToFront = (e.Side == EdgeSide.esLeft);
            if (e.OutIdx < 0)
            {
                var outRec = CreateOutRec();
                outRec.IsOpen = (e.WindDelta == 0);
                var newOp = new OutPt();
                outRec.Pts = newOp;
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = newOp;
                newOp.Prev = newOp;
                if (!outRec.IsOpen)
                    SetHoleState(e, outRec);
                e.OutIdx = outRec.Idx; //nb: do this after SetZ !
                return newOp;
            }
            else
            {
                var outRec = m_PolyOuts[e.OutIdx];
                //OutRec.Pts is the 'Left-most' point & OutRec.Pts.Prev is the 'Right-most'
                var op = outRec.Pts;
                if (ToFront && pt == op.Pt) return op;
                if (!ToFront && pt == op.Prev.Pt) return op.Prev;

                var newOp = new OutPt
                {
                    Idx = outRec.Idx,
                    Pt = pt,
                    Next = op,
                    Prev = op.Prev
                };
                newOp.Prev.Next = newOp;
                op.Prev = newOp;
                if (ToFront) outRec.Pts = newOp;
                return newOp;
            }
        }

        //------------------------------------------------------------------------------

        private bool HorzSegmentsOverlap(float seg1a, float seg1b, float seg2a, float seg2b)
        {
            if (seg1a > seg1b) Swap(ref seg1a, ref seg1b);
            if (seg2a > seg2b) Swap(ref seg2a, ref seg2b);
            return (seg1a < seg2b) && (seg2a < seg1b);
        }

        //------------------------------------------------------------------------------

        private void SetHoleState(TEdge e, OutRec outRec)
        {
            var isHole = false;
            var e2 = e.PrevInAEL;
            while (e2 != null)
            {
                if (e2.OutIdx >= 0 && e2.WindDelta != 0)
                {
                    isHole = !isHole;
                    if (outRec.FirstLeft == null)
                        outRec.FirstLeft = m_PolyOuts[e2.OutIdx];
                }
                e2 = e2.PrevInAEL;
            }
            if (isHole)
                outRec.IsHole = true;
        }

        //------------------------------------------------------------------------------

        private static float GetDx(Vector2 pt1, Vector2 pt2)
        {
            if (Calc.NearEqual(pt1.Y, pt2.Y)) return Horizontal;
            return (pt2.X - pt1.X)/(pt2.Y - pt1.Y);
        }

        //---------------------------------------------------------------------------

        private bool FirstIsBottomPt(OutPt btmPt1, OutPt btmPt2)
        {
            var p = btmPt1.Prev;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Prev;
            var dx1p = Math.Abs(GetDx(btmPt1.Pt, p.Pt));
            p = btmPt1.Next;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Next;
            var dx1n = Math.Abs(GetDx(btmPt1.Pt, p.Pt));

            p = btmPt2.Prev;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Prev;
            var dx2p = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            p = btmPt2.Next;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Next;
            var dx2n = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            return (dx1p >= dx2p && dx1p >= dx2n) || (dx1n >= dx2p && dx1n >= dx2n);
        }

        //------------------------------------------------------------------------------

        private OutPt GetBottomPt(OutPt pp)
        {
            OutPt dups = null;
            var p = pp.Next;
            while (p != pp)
            {
                if (p.Pt.Y > pp.Pt.Y)
                {
                    pp = p;
                    dups = null;
                }
                else if (Calc.NearEqual(p.Pt.Y, pp.Pt.Y) && p.Pt.X <= pp.Pt.X)
                {
                    if (p.Pt.X < pp.Pt.X)
                    {
                        dups = null;
                        pp = p;
                    }
                    else
                    {
                        if (p.Next != pp && p.Prev != pp) dups = p;
                    }
                }
                p = p.Next;
            }
            if (dups != null)
            {
                //there appears to be at least 2 vertices at bottomPt so ...
                while (dups != p)
                {
                    if (!FirstIsBottomPt(p, dups)) pp = dups;
                    dups = dups.Next;
                    while (dups.Pt != pp.Pt) dups = dups.Next;
                }
            }
            return pp;
        }

        //------------------------------------------------------------------------------

        private OutRec GetLowermostRec(OutRec outRec1, OutRec outRec2)
        {
            //work out which polygon fragment has the correct hole state ...
            if (outRec1.BottomPt == null)
                outRec1.BottomPt = GetBottomPt(outRec1.Pts);
            if (outRec2.BottomPt == null)
                outRec2.BottomPt = GetBottomPt(outRec2.Pts);
            var bPt1 = outRec1.BottomPt;
            var bPt2 = outRec2.BottomPt;
            if (bPt1.Pt.Y > bPt2.Pt.Y) return outRec1;
            if (bPt1.Pt.Y < bPt2.Pt.Y) return outRec2;
            if (bPt1.Pt.X < bPt2.Pt.X) return outRec1;
            if (bPt1.Pt.X > bPt2.Pt.X) return outRec2;
            if (bPt1.Next == bPt1) return outRec2;
            if (bPt2.Next == bPt2) return outRec1;
            if (FirstIsBottomPt(bPt1, bPt2)) return outRec1;
            return outRec2;
        }

        //------------------------------------------------------------------------------

        private static bool Param1RightOfParam2(OutRec outRec1, OutRec outRec2)
        {
            do
            {
                outRec1 = outRec1.FirstLeft;
                if (outRec1 == outRec2) return true;
            } while (outRec1 != null);
            return false;
        }

        //------------------------------------------------------------------------------

        private OutRec GetOutRec(int idx)
        {
            var outrec = m_PolyOuts[idx];
            while (outrec != m_PolyOuts[outrec.Idx])
                outrec = m_PolyOuts[outrec.Idx];
            return outrec;
        }

        //------------------------------------------------------------------------------

        private void AppendPolygon(TEdge e1, TEdge e2)
        {
            //get the start and ends of both output polygons ...
            var outRec1 = m_PolyOuts[e1.OutIdx];
            var outRec2 = m_PolyOuts[e2.OutIdx];

            OutRec holeStateRec;
            if (Param1RightOfParam2(outRec1, outRec2))
                holeStateRec = outRec2;
            else if (Param1RightOfParam2(outRec2, outRec1))
                holeStateRec = outRec1;
            else
                holeStateRec = GetLowermostRec(outRec1, outRec2);

            var p1_lft = outRec1.Pts;
            var p1_rt = p1_lft.Prev;
            var p2_lft = outRec2.Pts;
            var p2_rt = p2_lft.Prev;

            EdgeSide side;
            //join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.esLeft)
            {
                if (e2.Side == EdgeSide.esLeft)
                {
                    //z y x a b c
                    ReversePolyPtLinks(p2_lft);
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    outRec1.Pts = p2_rt;
                }
                else
                {
                    //x y z a b c
                    p2_rt.Next = p1_lft;
                    p1_lft.Prev = p2_rt;
                    p2_lft.Prev = p1_rt;
                    p1_rt.Next = p2_lft;
                    outRec1.Pts = p2_lft;
                }
                side = EdgeSide.esLeft;
            }
            else
            {
                if (e2.Side == EdgeSide.esRight)
                {
                    //a b c z y x
                    ReversePolyPtLinks(p2_lft);
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                }
                else
                {
                    //a b c x y z
                    p1_rt.Next = p2_lft;
                    p2_lft.Prev = p1_rt;
                    p1_lft.Prev = p2_rt;
                    p2_rt.Next = p1_lft;
                }
                side = EdgeSide.esRight;
            }

            outRec1.BottomPt = null;
            if (holeStateRec == outRec2)
            {
                if (outRec2.FirstLeft != outRec1)
                    outRec1.FirstLeft = outRec2.FirstLeft;
                outRec1.IsHole = outRec2.IsHole;
            }
            outRec2.Pts = null;
            outRec2.BottomPt = null;

            outRec2.FirstLeft = outRec1;

            var OKIdx = e1.OutIdx;
            var ObsoleteIdx = e2.OutIdx;

            e1.OutIdx = Unassigned; //nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIdx = Unassigned;

            var e = m_ActiveEdges;
            while (e != null)
            {
                if (e.OutIdx == ObsoleteIdx)
                {
                    e.OutIdx = OKIdx;
                    e.Side = side;
                    break;
                }
                e = e.NextInAEL;
            }
            outRec2.Idx = outRec1.Idx;
        }

        //------------------------------------------------------------------------------

        private static void ReversePolyPtLinks(OutPt pp)
        {
            if (pp == null) return;
            OutPt pp1 = pp;
            do
            {
                OutPt pp2 = pp1.Next;
                pp1.Next = pp1.Prev;
                pp1.Prev = pp2;
                pp1 = pp2;
            } while (pp1 != pp);
        }

        //------------------------------------------------------------------------------

        private static void SwapSides(TEdge edge1, TEdge edge2)
        {
            var side = edge1.Side;
            edge1.Side = edge2.Side;
            edge2.Side = side;
        }

        //------------------------------------------------------------------------------

        private static void SwapPolyIndexes(TEdge edge1, TEdge edge2)
        {
            var outIdx = edge1.OutIdx;
            edge1.OutIdx = edge2.OutIdx;
            edge2.OutIdx = outIdx;
        }

        //------------------------------------------------------------------------------

        private void IntersectEdges(TEdge e1, TEdge e2, Vector2 pt)
        {
            //e1 will be to the left of e2 BELOW the intersection. Therefore e1 is before
            //e2 in AEL except when e1 is being inserted at the intersection point ...

            var e1Contributing = (e1.OutIdx >= 0);
            var e2Contributing = (e2.OutIdx >= 0);

            //update winding counts...
            //assumes that e1 will be to the Right of e2 ABOVE the intersection
            if (e1.PolyTyp == e2.PolyTyp)
            {
                if (IsEvenOddFillType(e1))
                {
                    var oldE1WindCnt = e1.WindCnt;
                    e1.WindCnt = e2.WindCnt;
                    e2.WindCnt = oldE1WindCnt;
                }
                else
                {
                    if (e1.WindCnt + e2.WindDelta == 0) e1.WindCnt = -e1.WindCnt;
                    else e1.WindCnt += e2.WindDelta;
                    if (e2.WindCnt - e1.WindDelta == 0) e2.WindCnt = -e2.WindCnt;
                    else e2.WindCnt -= e1.WindDelta;
                }
            }
            else
            {
                if (!IsEvenOddFillType(e2)) e1.WindCnt2 += e2.WindDelta;
                else e1.WindCnt2 = (e1.WindCnt2 == 0) ? 1 : 0;
                if (!IsEvenOddFillType(e1)) e2.WindCnt2 -= e1.WindDelta;
                else e2.WindCnt2 = (e2.WindCnt2 == 0) ? 1 : 0;
            }

            PolyFillType e1FillType, e2FillType, e1FillType2, e2FillType2;
            if (e1.PolyTyp == PolyType.ptSubject)
            {
                e1FillType = m_SubjFillType;
                e1FillType2 = m_ClipFillType;
            }
            else
            {
                e1FillType = m_ClipFillType;
                e1FillType2 = m_SubjFillType;
            }
            if (e2.PolyTyp == PolyType.ptSubject)
            {
                e2FillType = m_SubjFillType;
                e2FillType2 = m_ClipFillType;
            }
            else
            {
                e2FillType = m_ClipFillType;
                e2FillType2 = m_SubjFillType;
            }

            int e1Wc, e2Wc;
            switch (e1FillType)
            {
                case PolyFillType.pftPositive:
                    e1Wc = e1.WindCnt;
                    break;
                case PolyFillType.pftNegative:
                    e1Wc = -e1.WindCnt;
                    break;
                default:
                    e1Wc = Math.Abs(e1.WindCnt);
                    break;
            }
            switch (e2FillType)
            {
                case PolyFillType.pftPositive:
                    e2Wc = e2.WindCnt;
                    break;
                case PolyFillType.pftNegative:
                    e2Wc = -e2.WindCnt;
                    break;
                default:
                    e2Wc = Math.Abs(e2.WindCnt);
                    break;
            }

            if (e1Contributing && e2Contributing)
            {
                if ((e1Wc != 0 && e1Wc != 1) || (e2Wc != 0 && e2Wc != 1) ||
                    (e1.PolyTyp != e2.PolyTyp && m_ClipType != ClipType.ctXor))
                {
                    AddLocalMaxPoly(e1, e2, pt);
                }
                else
                {
                    AddOutPt(e1, pt);
                    AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if (e1Contributing)
            {
                if (e2Wc == 0 || e2Wc == 1)
                {
                    AddOutPt(e1, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if (e2Contributing)
            {
                if (e1Wc == 0 || e1Wc == 1)
                {
                    AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if ((e1Wc == 0 || e1Wc == 1) && (e2Wc == 0 || e2Wc == 1))
            {
                //neither edge is currently contributing ...
                int e1Wc2, e2Wc2;
                switch (e1FillType2)
                {
                    case PolyFillType.pftPositive:
                        e1Wc2 = e1.WindCnt2;
                        break;
                    case PolyFillType.pftNegative:
                        e1Wc2 = -e1.WindCnt2;
                        break;
                    default:
                        e1Wc2 = Math.Abs(e1.WindCnt2);
                        break;
                }
                switch (e2FillType2)
                {
                    case PolyFillType.pftPositive:
                        e2Wc2 = e2.WindCnt2;
                        break;
                    case PolyFillType.pftNegative:
                        e2Wc2 = -e2.WindCnt2;
                        break;
                    default:
                        e2Wc2 = Math.Abs(e2.WindCnt2);
                        break;
                }

                if (e1.PolyTyp != e2.PolyTyp)
                {
                    AddLocalMinPoly(e1, e2, pt);
                }
                else if (e1Wc == 1 && e2Wc == 1)
                    switch (m_ClipType)
                    {
                        case ClipType.ctIntersection:
                            if (e1Wc2 > 0 && e2Wc2 > 0)
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.ctUnion:
                            if (e1Wc2 <= 0 && e2Wc2 <= 0)
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.ctDifference:
                            if (((e1.PolyTyp == PolyType.ptClip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                                ((e1.PolyTyp == PolyType.ptSubject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                                AddLocalMinPoly(e1, e2, pt);
                            break;
                        case ClipType.ctXor:
                            AddLocalMinPoly(e1, e2, pt);
                            break;
                    }
                else
                    SwapSides(e1, e2);
            }
        }

        //------------------------------------------------------------------------------

        private void DeleteFromAEL(TEdge e)
        {
            var AelPrev = e.PrevInAEL;
            var AelNext = e.NextInAEL;
            if (AelPrev == null && AelNext == null && (e != m_ActiveEdges))
                return; //already deleted
            if (AelPrev != null)
                AelPrev.NextInAEL = AelNext;
            else m_ActiveEdges = AelNext;
            if (AelNext != null)
                AelNext.PrevInAEL = AelPrev;
            e.NextInAEL = null;
            e.PrevInAEL = null;
        }

        //------------------------------------------------------------------------------

        private void DeleteFromSEL(TEdge e)
        {
            var SelPrev = e.PrevInSEL;
            var SelNext = e.NextInSEL;
            if (SelPrev == null && SelNext == null && (e != m_SortedEdges))
                return; //already deleted
            if (SelPrev != null)
                SelPrev.NextInSEL = SelNext;
            else m_SortedEdges = SelNext;
            if (SelNext != null)
                SelNext.PrevInSEL = SelPrev;
            e.NextInSEL = null;
            e.PrevInSEL = null;
        }

        //------------------------------------------------------------------------------

        private void UpdateEdgeIntoAEL(ref TEdge e)
        {
            if (e.NextInLML == null)
                throw new ClipperException("UpdateEdgeIntoAEL: invalid call");
            var AelPrev = e.PrevInAEL;
            var AelNext = e.NextInAEL;
            e.NextInLML.OutIdx = e.OutIdx;
            if (AelPrev != null)
                AelPrev.NextInAEL = e.NextInLML;
            else m_ActiveEdges = e.NextInLML;
            if (AelNext != null)
                AelNext.PrevInAEL = e.NextInLML;
            e.NextInLML.Side = e.Side;
            e.NextInLML.WindDelta = e.WindDelta;
            e.NextInLML.WindCnt = e.WindCnt;
            e.NextInLML.WindCnt2 = e.WindCnt2;
            e = e.NextInLML;
            e.Curr = e.Bot;
            e.PrevInAEL = AelPrev;
            e.NextInAEL = AelNext;
            if (!IsHorizontal(e)) InsertScanbeam(e.Top.Y);
        }

        //------------------------------------------------------------------------------

        private void ProcessHorizontals(bool isTopOfScanbeam)
        {
            var horzEdge = m_SortedEdges;
            while (horzEdge != null)
            {
                DeleteFromSEL(horzEdge);
                ProcessHorizontal(horzEdge, isTopOfScanbeam);
                horzEdge = m_SortedEdges;
            }
        }

        //------------------------------------------------------------------------------

        private static void GetHorzDirection(TEdge HorzEdge, out Direction Dir, out float Left, out float Right)
        {
            if (HorzEdge.Bot.X < HorzEdge.Top.X)
            {
                Left = HorzEdge.Bot.X;
                Right = HorzEdge.Top.X;
                Dir = Direction.dLeftToRight;
            }
            else
            {
                Left = HorzEdge.Top.X;
                Right = HorzEdge.Bot.X;
                Dir = Direction.dRightToLeft;
            }
        }

        //------------------------------------------------------------------------

        private void ProcessHorizontal(TEdge horzEdge, bool isTopOfScanbeam)
        {
            Direction dir;
            float horzLeft, horzRight;

            GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);

            TEdge eLastHorz = horzEdge, eMaxPair = null;
            while (eLastHorz.NextInLML != null && IsHorizontal(eLastHorz.NextInLML))
                eLastHorz = eLastHorz.NextInLML;
            if (eLastHorz.NextInLML == null)
                eMaxPair = GetMaximaPair(eLastHorz);

            for (;;)
            {
                var IsLastHorz = (horzEdge == eLastHorz);
                var e = GetNextInAEL(horzEdge, dir);
                while (e != null)
                {
                    //Break if we've got to the end of an intermediate horizontal edge ...
                    //nb: Smaller Dx's are to the right of larger Dx's ABOVE the horizontal.
                    if (Calc.NearEqual(e.Curr.X, horzEdge.Top.X) && horzEdge.NextInLML != null &&
                        e.Dx < horzEdge.NextInLML.Dx) break;

                    var eNext = GetNextInAEL(e, dir); //saves eNext for later

                    if ((dir == Direction.dLeftToRight && e.Curr.X <= horzRight) ||
                        (dir == Direction.dRightToLeft && e.Curr.X >= horzLeft))
                    {
                        //so far we're still in range of the horizontal Edge  but make sure
                        //we're at the last of consec. horizontals when matching with eMaxPair
                        if (e == eMaxPair && IsLastHorz)
                        {
                            if (horzEdge.OutIdx >= 0)
                            {
                                var op1 = AddOutPt(horzEdge, horzEdge.Top);
                                var eNextHorz = m_SortedEdges;
                                while (eNextHorz != null)
                                {
                                    if (eNextHorz.OutIdx >= 0 &&
                                        HorzSegmentsOverlap(horzEdge.Bot.X,
                                            horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                                    {
                                        OutPt op2 = AddOutPt(eNextHorz, eNextHorz.Bot);
                                        AddJoin(op2, op1, eNextHorz.Top);
                                    }
                                    eNextHorz = eNextHorz.NextInSEL;
                                }
                                AddGhostJoin(op1, horzEdge.Bot);
                                AddLocalMaxPoly(horzEdge, eMaxPair, horzEdge.Top);
                            }
                            DeleteFromAEL(horzEdge);
                            DeleteFromAEL(eMaxPair);
                            return;
                        }
                        if (dir == Direction.dLeftToRight)
                        {
                            var Pt = new Vector2(e.Curr.X, horzEdge.Curr.Y);
                            IntersectEdges(horzEdge, e, Pt);
                        }
                        else
                        {
                            var Pt = new Vector2(e.Curr.X, horzEdge.Curr.Y);
                            IntersectEdges(e, horzEdge, Pt);
                        }
                        SwapPositionsInAEL(horzEdge, e);
                    }
                    else if ((dir == Direction.dLeftToRight && e.Curr.X >= horzRight) ||
                             (dir == Direction.dRightToLeft && e.Curr.X <= horzLeft)) break;
                    e = eNext;
                } //end while

                if (horzEdge.NextInLML != null && IsHorizontal(horzEdge.NextInLML))
                {
                    UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.OutIdx >= 0) AddOutPt(horzEdge, horzEdge.Bot);
                    GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);
                }
                else
                    break;
            } //end for (;;)

            if (horzEdge.NextInLML != null)
            {
                if (horzEdge.OutIdx >= 0)
                {
                    OutPt op1 = AddOutPt(horzEdge, horzEdge.Top);
                    if (isTopOfScanbeam) AddGhostJoin(op1, horzEdge.Bot);

                    UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.WindDelta == 0) return;
                    //nb: HorzEdge is no longer horizontal here
                    var ePrev = horzEdge.PrevInAEL;
                    var eNext = horzEdge.NextInAEL;
                    if (ePrev != null && Calc.NearEqual(ePrev.Curr.X, horzEdge.Bot.X) &&
                        Calc.NearEqual(ePrev.Curr.Y, horzEdge.Bot.Y) && ePrev.WindDelta != 0 &&
                        (ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                         SlopesEqual(horzEdge, ePrev, m_UseFullRange)))
                    {
                        OutPt op2 = AddOutPt(ePrev, horzEdge.Bot);
                        AddJoin(op1, op2, horzEdge.Top);
                    }
                    else if (eNext != null && Calc.NearEqual(eNext.Curr.X, horzEdge.Bot.X) &&
                             Calc.NearEqual(eNext.Curr.Y, horzEdge.Bot.Y) && eNext.WindDelta != 0 &&
                             eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                             SlopesEqual(horzEdge, eNext, m_UseFullRange))
                    {
                        OutPt op2 = AddOutPt(eNext, horzEdge.Bot);
                        AddJoin(op1, op2, horzEdge.Top);
                    }
                }
                else
                    UpdateEdgeIntoAEL(ref horzEdge);
            }
            else
            {
                if (horzEdge.OutIdx >= 0) AddOutPt(horzEdge, horzEdge.Top);
                DeleteFromAEL(horzEdge);
            }
        }

        //------------------------------------------------------------------------------

        private static TEdge GetNextInAEL(TEdge e, Direction Direction)
        {
            return Direction == Direction.dLeftToRight ? e.NextInAEL : e.PrevInAEL;
        }

        //------------------------------------------------------------------------------

        private static bool IsMaxima(TEdge e, float y)
        {
            return (e != null && Calc.NearEqual(e.Top.Y, y) && e.NextInLML == null);
        }

        //------------------------------------------------------------------------------

        private static bool IsIntermediate(TEdge e, float y)
        {
            return (Calc.NearEqual(e.Top.Y, y) && e.NextInLML != null);
        }

        //------------------------------------------------------------------------------

        private static TEdge GetMaximaPair(TEdge e)
        {
            TEdge result = null;
            if ((e.Next.Top == e.Top) && e.Next.NextInLML == null)
                result = e.Next;
            else if ((e.Prev.Top == e.Top) && e.Prev.NextInLML == null)
                result = e.Prev;
            if (result != null && (result.OutIdx == Skip ||
                                   (result.NextInAEL == result.PrevInAEL && !IsHorizontal(result))))
                return null;
            return result;
        }

        //------------------------------------------------------------------------------

        private bool ProcessIntersections(float topY)
        {
            if (m_ActiveEdges == null) return true;
            try
            {
                BuildIntersectList(topY);
                if (m_IntersectList.Count == 0) return true;
                if (m_IntersectList.Count == 1 || FixupIntersectionOrder())
                    ProcessIntersectList();
                else
                    return false;
            }
            catch
            {
                m_SortedEdges = null;
                m_IntersectList.Clear();
                throw new ClipperException("ProcessIntersections error");
            }
            m_SortedEdges = null;
            return true;
        }

        //------------------------------------------------------------------------------

        private void BuildIntersectList(float topY)
        {
            if (m_ActiveEdges == null) return;

            //prepare for sorting ...
            var e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e != null)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e.Curr.X = TopX(e, topY);
                e = e.NextInAEL;
            }

            //bubblesort ...
            var isModified = true;
            while (isModified && m_SortedEdges != null)
            {
                isModified = false;
                e = m_SortedEdges;
                while (e.NextInSEL != null)
                {
                    var eNext = e.NextInSEL;
                    if (e.Curr.X > eNext.Curr.X)
                    {
                        Vector2 pt;
                        IntersectPoint(e, eNext, out pt);
                        var newNode = new IntersectNode
                        {
                            Edge1 = e,
                            Edge2 = eNext,
                            Pt = pt
                        };
                        m_IntersectList.Add(newNode);

                        SwapPositionsInSEL(e, eNext);
                        isModified = true;
                    }
                    else
                        e = eNext;
                }
                if (e.PrevInSEL != null) e.PrevInSEL.NextInSEL = null;
                else break;
            }
            m_SortedEdges = null;
        }

        //------------------------------------------------------------------------------

        private static bool EdgesAdjacent(IntersectNode inode)
        {
            return (inode.Edge1.NextInSEL == inode.Edge2) ||
                   (inode.Edge1.PrevInSEL == inode.Edge2);
        }

        //------------------------------------------------------------------------------

        private bool FixupIntersectionOrder()
        {
            //pre-condition: intersections are sorted bottom-most first.
            //Now it's crucial that intersections are made only between adjacent edges,
            //so to ensure this the order of intersections may need adjusting ...
            m_IntersectList.Sort(m_IntersectNodeComparer);

            CopyAELToSEL();
            var cnt = m_IntersectList.Count;
            for (var i = 0; i < cnt; i++)
            {
                if (!EdgesAdjacent(m_IntersectList[i]))
                {
                    var j = i + 1;
                    while (j < cnt && !EdgesAdjacent(m_IntersectList[j])) j++;
                    if (j == cnt) return false;

                    var tmp = m_IntersectList[i];
                    m_IntersectList[i] = m_IntersectList[j];
                    m_IntersectList[j] = tmp;
                }
                SwapPositionsInSEL(m_IntersectList[i].Edge1, m_IntersectList[i].Edge2);
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private void ProcessIntersectList()
        {
            for (var i = 0; i < m_IntersectList.Count; i++)
            {
                var iNode = m_IntersectList[i];
                {
                    IntersectEdges(iNode.Edge1, iNode.Edge2, iNode.Pt);
                    SwapPositionsInAEL(iNode.Edge1, iNode.Edge2);
                }
            }
            m_IntersectList.Clear();
        }

        //------------------------------------------------------------------------------

        internal static int Round(float value)
        {
            return value < 0 ? (int) (value - 0.5) : (int) (value + 0.5);
        }

        //------------------------------------------------------------------------------

        private static float TopX(TEdge edge, float currentY)
        {
            if (Calc.NearEqual(currentY, edge.Top.Y))
                return edge.Top.X;
            return edge.Bot.X + Round(edge.Dx*(currentY - edge.Bot.Y));
        }

        //------------------------------------------------------------------------------

        private static void IntersectPoint(TEdge edge1, TEdge edge2, out Vector2 ip)
        {
            ip = new Vector2();
            float b1, b2;
            //nb: with very large coordinate values, it's possible for SlopesEqual() to 
            //return false but for the edge.Dx value be equal due to float precision rounding.
            if (Calc.NearEqual(edge1.Dx, edge2.Dx))
            {
                ip.Y = edge1.Curr.Y;
                ip.X = TopX(edge1, ip.Y);
                return;
            }

            if (Calc.NearZero(edge1.Delta.X))
            {
                ip.X = edge1.Bot.X;
                if (IsHorizontal(edge2))
                {
                    ip.Y = edge2.Bot.Y;
                }
                else
                {
                    b2 = edge2.Bot.Y - (edge2.Bot.X/edge2.Dx);
                    ip.Y = Round(ip.X/edge2.Dx + b2);
                }
            }
            else if (Calc.NearZero(edge2.Delta.X))
            {
                ip.X = edge2.Bot.X;
                if (IsHorizontal(edge1))
                {
                    ip.Y = edge1.Bot.Y;
                }
                else
                {
                    b1 = edge1.Bot.Y - (edge1.Bot.X/edge1.Dx);
                    ip.Y = Round(ip.X/edge1.Dx + b1);
                }
            }
            else
            {
                b1 = edge1.Bot.X - edge1.Bot.Y*edge1.Dx;
                b2 = edge2.Bot.X - edge2.Bot.Y*edge2.Dx;
                var q = (b2 - b1)/(edge1.Dx - edge2.Dx);
                ip.Y = Round(q);
                ip.X = Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx) ? Round(edge1.Dx*q + b1) : Round(edge2.Dx*q + b2);
            }

            if (ip.Y < edge1.Top.Y || ip.Y < edge2.Top.Y)
            {
                ip.Y = edge1.Top.Y > edge2.Top.Y ? edge1.Top.Y : edge2.Top.Y;
                ip.X = TopX(Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx) ? edge1 : edge2, ip.Y);
            }
            //finally, don't allow 'ip' to be BELOW curr.Y (ie bottom of scanbeam) ...
            if (ip.Y > edge1.Curr.Y)
            {
                ip.Y = edge1.Curr.Y;
                //better to use the more vertical edge to derive X ...
                ip.X = TopX(Math.Abs(edge1.Dx) > Math.Abs(edge2.Dx) ? edge2 : edge1, ip.Y);
            }
        }

        //------------------------------------------------------------------------------

        private void ProcessEdgesAtTopOfScanbeam(float topY)
        {
            var e = m_ActiveEdges;
            while (e != null)
            {
                //1. process maxima, treating them as if they're 'bent' horizontal edges,
                //   but exclude maxima with horizontal edges. nb: e can't be a horizontal.
                var IsMaximaEdge = IsMaxima(e, topY);

                if (IsMaximaEdge)
                {
                    var eMaxPair = GetMaximaPair(e);
                    IsMaximaEdge = (eMaxPair == null || !IsHorizontal(eMaxPair));
                }

                if (IsMaximaEdge)
                {
                    var ePrev = e.PrevInAEL;
                    DoMaxima(e);
                    e = ePrev == null ? m_ActiveEdges : ePrev.NextInAEL;
                }
                else
                {
                    //2. promote horizontal edges, otherwise update Curr.X and Curr.Y ...
                    if (IsIntermediate(e, topY) && IsHorizontal(e.NextInLML))
                    {
                        UpdateEdgeIntoAEL(ref e);
                        if (e.OutIdx >= 0)
                            AddOutPt(e, e.Bot);
                        AddEdgeToSEL(e);
                    }
                    else
                    {
                        e.Curr.X = TopX(e, topY);
                        e.Curr.Y = topY;
                    }

                    if (StrictlySimple)
                    {
                        var ePrev = e.PrevInAEL;
                        if ((e.OutIdx >= 0) && (e.WindDelta != 0) && ePrev != null &&
                            (ePrev.OutIdx >= 0) && Calc.NearEqual(ePrev.Curr.X, e.Curr.X) &&
                            (ePrev.WindDelta != 0))
                        {
                            var ip = e.Curr;
                            var op = AddOutPt(ePrev, ip);
                            var op2 = AddOutPt(e, ip);
                            AddJoin(op, op2, ip); //StrictlySimple (type-3) join
                        }
                    }

                    e = e.NextInAEL;
                }
            }

            //3. Process horizontals at the Top of the scanbeam ...
            ProcessHorizontals(true);

            //4. Promote intermediate vertices ...
            e = m_ActiveEdges;
            while (e != null)
            {
                if (IsIntermediate(e, topY))
                {
                    OutPt op = null;
                    if (e.OutIdx >= 0)
                        op = AddOutPt(e, e.Top);
                    UpdateEdgeIntoAEL(ref e);

                    //if output polygons share an edge, they'll need joining later ...
                    var ePrev = e.PrevInAEL;
                    var eNext = e.NextInAEL;
                    if (ePrev != null && Calc.NearEqual(ePrev.Curr.X, e.Bot.X) &&
                        Calc.NearEqual(ePrev.Curr.Y, e.Bot.Y) && op != null &&
                        ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                        SlopesEqual(e, ePrev, m_UseFullRange) &&
                        (e.WindDelta != 0) && (ePrev.WindDelta != 0))
                    {
                        var op2 = AddOutPt(ePrev, e.Bot);
                        AddJoin(op, op2, e.Top);
                    }
                    else if (eNext != null && Calc.NearEqual(eNext.Curr.X, e.Bot.X) &&
                             Calc.NearEqual(eNext.Curr.Y, e.Bot.Y) && op != null &&
                             eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                             SlopesEqual(e, eNext, m_UseFullRange) &&
                             (e.WindDelta != 0) && (eNext.WindDelta != 0))
                    {
                        var op2 = AddOutPt(eNext, e.Bot);
                        AddJoin(op, op2, e.Top);
                    }
                }
                e = e.NextInAEL;
            }
        }

        //------------------------------------------------------------------------------

        private void DoMaxima(TEdge e)
        {
            var eMaxPair = GetMaximaPair(e);
            if (eMaxPair == null)
            {
                if (e.OutIdx >= 0)
                    AddOutPt(e, e.Top);
                DeleteFromAEL(e);
                return;
            }

            var eNext = e.NextInAEL;
            while (eNext != null && eNext != eMaxPair)
            {
                IntersectEdges(e, eNext, e.Top);
                SwapPositionsInAEL(e, eNext);
                eNext = e.NextInAEL;
            }

            if (e.OutIdx == Unassigned && eMaxPair.OutIdx == Unassigned)
            {
                DeleteFromAEL(e);
                DeleteFromAEL(eMaxPair);
            }
            else if (e.OutIdx >= 0 && eMaxPair.OutIdx >= 0)
            {
                if (e.OutIdx >= 0) AddLocalMaxPoly(e, eMaxPair, e.Top);
                DeleteFromAEL(e);
                DeleteFromAEL(eMaxPair);
            }
            else throw new ClipperException("DoMaxima error");
        }

        //------------------------------------------------------------------------------

        public static void ReversePaths(Paths polys)
        {
            foreach (var poly in polys)
            {
                poly.Reverse();
            }
        }

        //------------------------------------------------------------------------------

        public static bool Orientation(Path poly)
        {
            return Area(poly) >= 0;
        }

        //------------------------------------------------------------------------------

        private static int PointCount(OutPt pts)
        {
            if (pts == null) return 0;
            var result = 0;
            var p = pts;
            do
            {
                result++;
                p = p.Next;
            } while (p != pts);
            return result;
        }

        //------------------------------------------------------------------------------

        private void BuildResult(Paths polyg)
        {
            polyg.Clear();
            polyg.Capacity = m_PolyOuts.Count;
            for (var i = 0; i < m_PolyOuts.Count; i++)
            {
                var outRec = m_PolyOuts[i];
                if (outRec.Pts == null) continue;
                var p = outRec.Pts.Prev;
                var cnt = PointCount(p);
                if (cnt < 2) continue;
                var pg = new Path(cnt);
                for (var j = 0; j < cnt; j++)
                {
                    pg.Add(p.Pt);
                    p = p.Prev;
                }
                polyg.Add(pg);
            }
        }

        //------------------------------------------------------------------------------

        private void BuildResult2(PolyTree polytree)
        {
            polytree.Clear();

            //add each output polygon/contour to polytree ...
            polytree.m_AllPolys.Capacity = m_PolyOuts.Count;
            for (var i = 0; i < m_PolyOuts.Count; i++)
            {
                var outRec = m_PolyOuts[i];
                var cnt = PointCount(outRec.Pts);
                if ((outRec.IsOpen && cnt < 2) ||
                    (!outRec.IsOpen && cnt < 3)) continue;
                FixHoleLinkage(outRec);
                var pn = new PolyNode();
                polytree.m_AllPolys.Add(pn);
                outRec.PolyNode = pn;
                pn.m_polygon.Capacity = cnt;
                var op = outRec.Pts.Prev;
                for (var j = 0; j < cnt; j++)
                {
                    pn.m_polygon.Add(op.Pt);
                    op = op.Prev;
                }
            }

            //fixup PolyNode links etc ...
            polytree.m_Childs.Capacity = m_PolyOuts.Count;
            for (var i = 0; i < m_PolyOuts.Count; i++)
            {
                var outRec = m_PolyOuts[i];
                if (outRec.PolyNode == null) continue;
                if (outRec.IsOpen)
                {
                    outRec.PolyNode.IsOpen = true;
                    polytree.AddChild(outRec.PolyNode);
                }
                else if (outRec.FirstLeft?.PolyNode != null)
                    outRec.FirstLeft.PolyNode.AddChild(outRec.PolyNode);
                else
                    polytree.AddChild(outRec.PolyNode);
            }
        }

        //------------------------------------------------------------------------------

        private void FixupOutPolygon(OutRec outRec)
        {
            //FixupOutPolygon() - removes duplicate points and simplifies consecutive
            //parallel edges by removing the middle vertex.
            OutPt lastOK = null;
            outRec.BottomPt = null;
            var pp = outRec.Pts;
            for (;;)
            {
                if (pp.Prev == pp || pp.Prev == pp.Next)
                {
                    outRec.Pts = null;
                    return;
                }
                //test for duplicate points and collinear edges ...
                if ((pp.Pt == pp.Next.Pt) || (pp.Pt == pp.Prev.Pt) ||
                    (SlopesEqual(pp.Prev.Pt, pp.Pt, pp.Next.Pt, m_UseFullRange) &&
                     (!PreserveCollinear || !Pt2IsBetweenPt1AndPt3(pp.Prev.Pt, pp.Pt, pp.Next.Pt))))
                {
                    lastOK = null;
                    pp.Prev.Next = pp.Next;
                    pp.Next.Prev = pp.Prev;
                    pp = pp.Prev;
                }
                else if (pp == lastOK) break;
                else
                {
                    if (lastOK == null) lastOK = pp;
                    pp = pp.Next;
                }
            }
            outRec.Pts = pp;
        }

        //------------------------------------------------------------------------------

        private static OutPt DupOutPt(OutPt outPt, bool insertAfter)
        {
            var result = new OutPt
            {
                Pt = outPt.Pt,
                Idx = outPt.Idx
            };
            if (insertAfter)
            {
                result.Next = outPt.Next;
                result.Prev = outPt;
                outPt.Next.Prev = result;
                outPt.Next = result;
            }
            else
            {
                result.Prev = outPt.Prev;
                result.Next = outPt;
                outPt.Prev.Next = result;
                outPt.Prev = result;
            }
            return result;
        }

        //------------------------------------------------------------------------------

        private static bool GetOverlap(float a1, float a2, float b1, float b2, out float Left, out float Right)
        {
            if (a1 < a2)
            {
                if (b1 < b2)
                {
                    Left = Math.Max(a1, b1);
                    Right = Math.Min(a2, b2);
                }
                else
                {
                    Left = Math.Max(a1, b2);
                    Right = Math.Min(a2, b1);
                }
            }
            else
            {
                if (b1 < b2)
                {
                    Left = Math.Max(a2, b1);
                    Right = Math.Min(a1, b2);
                }
                else
                {
                    Left = Math.Max(a2, b2);
                    Right = Math.Min(a1, b1);
                }
            }
            return Left < Right;
        }

        //------------------------------------------------------------------------------

        private static bool JoinHorz(OutPt op1, OutPt op1b, OutPt op2, OutPt op2b,
            Vector2 Pt, bool DiscardLeft)
        {
            var Dir1 = (op1.Pt.X > op1b.Pt.X
                ? Direction.dRightToLeft
                : Direction.dLeftToRight);
            var Dir2 = (op2.Pt.X > op2b.Pt.X
                ? Direction.dRightToLeft
                : Direction.dLeftToRight);
            if (Dir1 == Dir2) return false;

            //When DiscardLeft, we want Op1b to be on the Left of Op1, otherwise we
            //want Op1b to be on the Right. (And likewise with Op2 and Op2b.)
            //So, to facilitate this while inserting Op1b and Op2b ...
            //when DiscardLeft, make sure we're AT or RIGHT of Pt before adding Op1b,
            //otherwise make sure we're AT or LEFT of Pt. (Likewise with Op2b.)
            if (Dir1 == Direction.dLeftToRight)
            {
                while (op1.Next.Pt.X <= Pt.X &&
                       op1.Next.Pt.X >= op1.Pt.X && Calc.NearEqual(op1.Next.Pt.Y, Pt.Y))
                    op1 = op1.Next;
                if (DiscardLeft && !Calc.NearEqual(op1.Pt.X, Pt.X)) op1 = op1.Next;
                op1b = DupOutPt(op1, !DiscardLeft);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    op1b = DupOutPt(op1, !DiscardLeft);
                }
            }
            else
            {
                while (op1.Next.Pt.X >= Pt.X &&
                       op1.Next.Pt.X <= op1.Pt.X && Calc.NearEqual(op1.Next.Pt.Y, Pt.Y))
                    op1 = op1.Next;
                if (!DiscardLeft && !Calc.NearEqual(op1.Pt.X, Pt.X)) op1 = op1.Next;
                op1b = DupOutPt(op1, DiscardLeft);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    op1b = DupOutPt(op1, DiscardLeft);
                }
            }

            if (Dir2 == Direction.dLeftToRight)
            {
                while (op2.Next.Pt.X <= Pt.X &&
                       op2.Next.Pt.X >= op2.Pt.X && Calc.NearEqual(op2.Next.Pt.Y, Pt.Y))
                    op2 = op2.Next;
                if (DiscardLeft && !Calc.NearEqual(op2.Pt.X, Pt.X)) op2 = op2.Next;
                op2b = DupOutPt(op2, !DiscardLeft);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    op2b = DupOutPt(op2, !DiscardLeft);
                }                
            }
            else
            {
                while (op2.Next.Pt.X >= Pt.X &&
                       op2.Next.Pt.X <= op2.Pt.X && Calc.NearEqual(op2.Next.Pt.Y, Pt.Y))
                    op2 = op2.Next;
                if (!DiscardLeft && !Calc.NearEqual(op2.Pt.X, Pt.X)) op2 = op2.Next;
                op2b = DupOutPt(op2, DiscardLeft);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    op2b = DupOutPt(op2, DiscardLeft);
                }                
            }            

            if ((Dir1 == Direction.dLeftToRight) == DiscardLeft)
            {
                op1.Prev = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Prev = op1b;
            }
            else
            {
                op1.Next = op2;
                op2.Prev = op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private bool JoinPoints(Join j, OutRec outRec1, OutRec outRec2)
        {
            OutPt op1 = j.OutPt1, op1b;
            OutPt op2 = j.OutPt2, op2b;

            //There are 3 kinds of joins for output polygons ...
            //1. Horizontal joins where Join.OutPt1 & Join.OutPt2 are a vertices anywhere
            //along (horizontal) collinear edges (& Join.OffPt is on the same horizontal).
            //2. Non-horizontal joins where Join.OutPt1 & Join.OutPt2 are at the same
            //location at the Bottom of the overlapping segment (& Join.OffPt is above).
            //3. StrictlySimple joins where edges touch but are not collinear and where
            //Join.OutPt1, Join.OutPt2 & Join.OffPt all share the same point.
            bool isHorizontal = Calc.NearEqual(j.OutPt1.Pt.Y, j.OffPt.Y);

            if (isHorizontal && (j.OffPt == j.OutPt1.Pt) && (j.OffPt == j.OutPt2.Pt))
            {
                //Strictly Simple join ...
                if (outRec1 != outRec2) return false;
                op1b = j.OutPt1.Next;
                while (op1b != op1 && (op1b.Pt == j.OffPt))
                    op1b = op1b.Next;
                var reverse1 = (op1b.Pt.Y > j.OffPt.Y);
                op2b = j.OutPt2.Next;
                while (op2b != op2 && (op2b.Pt == j.OffPt))
                    op2b = op2b.Next;
                var reverse2 = (op2b.Pt.Y > j.OffPt.Y);
                if (reverse1 == reverse2) return false;
                if (reverse1)
                {
                    op1b = DupOutPt(op1, false);
                    op2b = DupOutPt(op2, true);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                op1b = DupOutPt(op1, true);
                op2b = DupOutPt(op2, false);
                op1.Next = op2;
                op2.Prev = op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;
                j.OutPt1 = op1;
                j.OutPt2 = op1b;
                return true;
            }
            if (isHorizontal)
            {
                //treat horizontal joins differently to non-horizontal joins since with
                //them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                //may be anywhere along the horizontal edge.
                op1b = op1;
                while (Calc.NearEqual(op1.Prev.Pt.Y, op1.Pt.Y) && op1.Prev != op1b && op1.Prev != op2)
                    op1 = op1.Prev;
                while (Calc.NearEqual(op1b.Next.Pt.Y, op1b.Pt.Y) && op1b.Next != op1 && op1b.Next != op2)
                    op1b = op1b.Next;
                if (op1b.Next == op1 || op1b.Next == op2) return false; //a flat 'polygon'

                op2b = op2;
                while (Calc.NearEqual(op2.Prev.Pt.Y, op2.Pt.Y) && op2.Prev != op2b && op2.Prev != op1b)
                    op2 = op2.Prev;
                while (Calc.NearEqual(op2b.Next.Pt.Y, op2b.Pt.Y) && op2b.Next != op2 && op2b.Next != op1)
                    op2b = op2b.Next;
                if (op2b.Next == op2 || op2b.Next == op1) return false; //a flat 'polygon'

                float Left, Right;
                //Op1 -. Op1b & Op2 -. Op2b are the extremites of the horizontal edges
                if (!GetOverlap(op1.Pt.X, op1b.Pt.X, op2.Pt.X, op2b.Pt.X, out Left, out Right))
                    return false;

                //DiscardLeftSide: when overlapping edges are joined, a spike will created
                //which needs to be cleaned up. However, we don't want Op1 or Op2 caught up
                //on the discard Side as either may still be needed for other joins ...
                Vector2 Pt;
                bool DiscardLeftSide;
                if (op1.Pt.X >= Left && op1.Pt.X <= Right)
                {
                    Pt = op1.Pt;
                    DiscardLeftSide = (op1.Pt.X > op1b.Pt.X);
                }
                else if (op2.Pt.X >= Left && op2.Pt.X <= Right)
                {
                    Pt = op2.Pt;
                    DiscardLeftSide = (op2.Pt.X > op2b.Pt.X);
                }
                else if (op1b.Pt.X >= Left && op1b.Pt.X <= Right)
                {
                    Pt = op1b.Pt;
                    DiscardLeftSide = op1b.Pt.X > op1.Pt.X;
                }
                else
                {
                    Pt = op2b.Pt;
                    DiscardLeftSide = (op2b.Pt.X > op2.Pt.X);
                }
                j.OutPt1 = op1;
                j.OutPt2 = op2;
                return JoinHorz(op1, op1b, op2, op2b, Pt, DiscardLeftSide);
            }
            //nb: For non-horizontal joins ...
            //    1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
            //    2. Jr.OutPt1.Pt > Jr.OffPt.Y

            //make sure the polygons are correctly oriented ...
            op1b = op1.Next;
            while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Next;
            var Reverse1 = ((op1b.Pt.Y > op1.Pt.Y) ||
                            !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, m_UseFullRange));
            if (Reverse1)
            {
                op1b = op1.Prev;
                while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Prev;
                if ((op1b.Pt.Y > op1.Pt.Y) ||
                    !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, m_UseFullRange)) return false;
            }
                        
            op2b = op2.Next;
            while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Next;
            var Reverse2 = ((op2b.Pt.Y > op2.Pt.Y) ||
                            !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, m_UseFullRange));
            if (Reverse2)
            {
                op2b = op2.Prev;
                while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Prev;
                if ((op2b.Pt.Y > op2.Pt.Y) ||
                    !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, m_UseFullRange)) return false;
            }

            if ((op1b == op1) || (op2b == op2) || (op1b == op2b) ||
                ((outRec1 == outRec2) && (Reverse1 == Reverse2))) return false;

            if (Reverse1)
            {
                op1b = DupOutPt(op1, false);
                op2b = DupOutPt(op2, true);
                op1.Prev = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Prev = op1b;
                j.OutPt1 = op1;
                j.OutPt2 = op1b;
                return true;
            }
            op1b = DupOutPt(op1, true);
            op2b = DupOutPt(op2, false);
            op1.Next = op2;
            op2.Prev = op1;
            op1b.Prev = op2b;
            op2b.Next = op1b;
            j.OutPt1 = op1;
            j.OutPt2 = op1b;
            return true;
        }

        //----------------------------------------------------------------------

        public static int PointInPolygon(Vector2 pt, Path path)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            int result = 0, cnt = path.Count;
            if (cnt < 3) return 0;
            var ip = path[0];
            for (var i = 1; i <= cnt; ++i)
            {
                var ipNext = (i == cnt ? path[0] : path[i]);
                if (Calc.NearEqual(ipNext.Y, pt.Y))
                {
                    if (Calc.NearEqual(ipNext.X, pt.X) || (Calc.NearEqual(ip.Y, pt.Y) &&
                                               ((ipNext.X > pt.X) == (ip.X < pt.X)))) return -1;
                }
                if ((ip.Y < pt.Y) != (ipNext.Y < pt.Y))
                {
                    if (ip.X >= pt.X)
                    {
                        if (ipNext.X > pt.X) result = 1 - result;
                        else
                        {
                            var d = (ip.X - pt.X)*(ipNext.Y - pt.Y) -
                                    (ipNext.X - pt.X)*(ip.Y - pt.Y);
                            if (Calc.NearZero(d)) return -1;
                            if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (ipNext.X > pt.X)
                        {
                            var d = (ip.X - pt.X)*(ipNext.Y - pt.Y) -
                                    (ipNext.X - pt.X)*(ip.Y - pt.Y);
                            if (Calc.NearZero(d)) return -1;
                            if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                }
                ip = ipNext;
            }
            return result;
        }

        //------------------------------------------------------------------------------

        private static int PointInPolygon(Vector2 pt, OutPt op)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            var result = 0;
            var startOp = op;
            float ptx = pt.X, pty = pt.Y;
            float poly0x = op.Pt.X, poly0y = op.Pt.Y;
            do
            {
                op = op.Next;
                float poly1x = op.Pt.X, poly1y = op.Pt.Y;

                if (Calc.NearEqual(poly1y, pty))
                {
                    if (Calc.NearEqual(poly1x, ptx) || (Calc.NearEqual(poly0y, pty) &&
                                            ((poly1x > ptx) == (poly0x < ptx)))) return -1;
                }
                if ((poly0y < pty) != (poly1y < pty))
                {
                    if (poly0x >= ptx)
                    {
                        if (poly1x > ptx) result = 1 - result;
                        else
                        {
                            var d = (poly0x - ptx)*(poly1y - pty) -
                                    (poly1x - ptx)*(poly0y - pty);
                            if (Calc.NearZero(d)) return -1;
                            if ((d > 0) == (poly1y > poly0y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (poly1x > ptx)
                        {
                            var d = (poly0x - ptx)*(poly1y - pty) -
                                    (poly1x - ptx)*(poly0y - pty);
                            if (Calc.NearZero(d)) return -1;
                            if ((d > 0) == (poly1y > poly0y)) result = 1 - result;
                        }
                    }
                }
                poly0x = poly1x;
                poly0y = poly1y;
            } while (startOp != op);
            return result;
        }

        //------------------------------------------------------------------------------

        private static bool Poly2ContainsPoly1(OutPt outPt1, OutPt outPt2)
        {
            var op = outPt1;
            do
            {
                //nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                var res = PointInPolygon(op.Pt, outPt2);
                if (res >= 0) return res > 0;
                op = op.Next;
            } while (op != outPt1);
            return true;
        }

        //----------------------------------------------------------------------

        private void FixupFirstLefts1(OutRec OldOutRec, OutRec NewOutRec)
        {
            for (var i = 0; i < m_PolyOuts.Count; i++)
            {
                var outRec = m_PolyOuts[i];
                if (outRec.Pts == null || outRec.FirstLeft == null) continue;
                var firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (firstLeft == OldOutRec)
                {
                    if (Poly2ContainsPoly1(outRec.Pts, NewOutRec.Pts))
                        outRec.FirstLeft = NewOutRec;
                }
            }
        }

        //----------------------------------------------------------------------

        private void FixupFirstLefts2(OutRec OldOutRec, OutRec NewOutRec)
        {
            foreach (OutRec outRec in m_PolyOuts)
                if (outRec.FirstLeft == OldOutRec) outRec.FirstLeft = NewOutRec;
        }

        //----------------------------------------------------------------------

        private static OutRec ParseFirstLeft(OutRec FirstLeft)
        {
            while (FirstLeft != null && FirstLeft.Pts == null)
                FirstLeft = FirstLeft.FirstLeft;
            return FirstLeft;
        }

        //------------------------------------------------------------------------------

        private void JoinCommonEdges()
        {
            for (var i = 0; i < m_Joins.Count; i++)
            {
                var join = m_Joins[i];

                var outRec1 = GetOutRec(join.OutPt1.Idx);
                var outRec2 = GetOutRec(join.OutPt2.Idx);

                if (outRec1.Pts == null || outRec2.Pts == null) continue;

                //get the polygon fragment with the correct hole state (FirstLeft)
                //before calling JoinPoints() ...
                OutRec holeStateRec;
                if (outRec1 == outRec2) holeStateRec = outRec1;
                else if (Param1RightOfParam2(outRec1, outRec2)) holeStateRec = outRec2;
                else if (Param1RightOfParam2(outRec2, outRec1)) holeStateRec = outRec1;
                else holeStateRec = GetLowermostRec(outRec1, outRec2);

                if (!JoinPoints(join, outRec1, outRec2)) continue;

                if (outRec1 == outRec2)
                {
                    //instead of joining two polygons, we've just created a new one by
                    //splitting one polygon into two.
                    outRec1.Pts = join.OutPt1;
                    outRec1.BottomPt = null;
                    outRec2 = CreateOutRec();
                    outRec2.Pts = join.OutPt2;

                    //update all OutRec2.Pts Idx's ...
                    UpdateOutPtIdxs(outRec2);

                    //We now need to check every OutRec.FirstLeft pointer. If it points
                    //to OutRec1 it may need to point to OutRec2 instead ...
                    if (m_UsingPolyTree)
                        for (var j = 0; j < m_PolyOuts.Count - 1; j++)
                        {
                            var oRec = m_PolyOuts[j];
                            if (oRec.Pts == null || ParseFirstLeft(oRec.FirstLeft) != outRec1 ||
                                oRec.IsHole == outRec1.IsHole) continue;
                            if (Poly2ContainsPoly1(oRec.Pts, join.OutPt2))
                                oRec.FirstLeft = outRec2;
                        }

                    if (Poly2ContainsPoly1(outRec2.Pts, outRec1.Pts))
                    {
                        //outRec2 is contained by outRec1 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;

                        //fixup FirstLeft pointers that may need reassigning to OutRec1
                        if (m_UsingPolyTree) FixupFirstLefts2(outRec2, outRec1);

                        if ((outRec2.IsHole ^ ReverseSolution) == (Area(outRec2) > 0))
                            ReversePolyPtLinks(outRec2.Pts);
                    }
                    else if (Poly2ContainsPoly1(outRec1.Pts, outRec2.Pts))
                    {
                        //outRec1 is contained by outRec2 ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec1.IsHole = !outRec2.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;
                        outRec1.FirstLeft = outRec2;

                        //fixup FirstLeft pointers that may need reassigning to OutRec1
                        if (m_UsingPolyTree) FixupFirstLefts2(outRec1, outRec2);

                        if ((outRec1.IsHole ^ ReverseSolution) == (Area(outRec1) > 0))
                            ReversePolyPtLinks(outRec1.Pts);
                    }
                    else
                    {
                        //the 2 polygons are completely separate ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;

                        //fixup FirstLeft pointers that may need reassigning to OutRec2
                        if (m_UsingPolyTree) FixupFirstLefts1(outRec1, outRec2);
                    }
                }
                else
                {
                    //joined 2 polygons together ...

                    outRec2.Pts = null;
                    outRec2.BottomPt = null;
                    outRec2.Idx = outRec1.Idx;

                    outRec1.IsHole = holeStateRec.IsHole;
                    if (holeStateRec == outRec2)
                        outRec1.FirstLeft = outRec2.FirstLeft;
                    outRec2.FirstLeft = outRec1;

                    //fixup FirstLeft pointers that may need reassigning to OutRec1
                    if (m_UsingPolyTree) FixupFirstLefts2(outRec2, outRec1);
                }
            }
        }

        //------------------------------------------------------------------------------

        private static void UpdateOutPtIdxs(OutRec outrec)
        {
            var op = outrec.Pts;
            do
            {
                op.Idx = outrec.Idx;
                op = op.Prev;
            } while (op != outrec.Pts);
        }

        //------------------------------------------------------------------------------

        private void DoSimplePolygons()
        {
            var i = 0;
            while (i < m_PolyOuts.Count)
            {
                var outrec = m_PolyOuts[i++];
                var op = outrec.Pts;
                if (op == null || outrec.IsOpen) continue;
                do //for each Pt in Polygon until duplicate found do ...
                {
                    var op2 = op.Next;
                    while (op2 != outrec.Pts)
                    {
                        if ((op.Pt == op2.Pt) && op2.Next != op && op2.Prev != op)
                        {
                            //split the polygon into two ...
                            var op3 = op.Prev;
                            var op4 = op2.Prev;
                            op.Prev = op4;
                            op4.Next = op;
                            op2.Prev = op3;
                            op3.Next = op2;

                            outrec.Pts = op;
                            OutRec outrec2 = CreateOutRec();
                            outrec2.Pts = op2;
                            UpdateOutPtIdxs(outrec2);
                            if (Poly2ContainsPoly1(outrec2.Pts, outrec.Pts))
                            {
                                //OutRec2 is contained by OutRec1 ...
                                outrec2.IsHole = !outrec.IsHole;
                                outrec2.FirstLeft = outrec;
                                if (m_UsingPolyTree) FixupFirstLefts2(outrec2, outrec);
                            }
                            else if (Poly2ContainsPoly1(outrec.Pts, outrec2.Pts))
                            {
                                //OutRec1 is contained by OutRec2 ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec.IsHole = !outrec2.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                outrec.FirstLeft = outrec2;
                                if (m_UsingPolyTree) FixupFirstLefts2(outrec, outrec2);
                            }
                            else
                            {
                                //the 2 polygons are separate ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                if (m_UsingPolyTree) FixupFirstLefts1(outrec, outrec2);
                            }
                            op2 = op; //ie get ready for the next iteration
                        }
                        op2 = op2.Next;
                    }
                    op = op.Next;
                } while (op != outrec.Pts);
            }
        }

        //------------------------------------------------------------------------------

        public static float Area(Path poly)
        {
            var cnt = poly.Count;
            if (cnt < 3) return 0;
            float a = 0;
            for (int i = 0, j = cnt - 1; i < cnt; ++i)
            {
                a += (poly[j].X + poly[i].X)*(poly[j].Y - poly[i].Y);
                j = i;
            }
            return -a*0.5f;
        }

        //------------------------------------------------------------------------------

        private static float Area(OutRec outRec)
        {
            var op = outRec.Pts;
            if (op == null) return 0;
            float a = 0;
            do
            {
                a = a + (op.Prev.Pt.X + op.Pt.X)*(op.Prev.Pt.Y - op.Pt.Y);
                op = op.Next;
            } while (op != outRec.Pts);
            return a*0.5f;
        }

        //------------------------------------------------------------------------------
        // SimplifyPolygon functions ...
        // Convert self-intersecting polygons into simple polygons
        //------------------------------------------------------------------------------

        public static Paths SimplifyPolygon(Path poly,
            PolyFillType fillType = PolyFillType.pftEvenOdd)
        {
            var result = new Paths();
            var c = new AngusjClipper {StrictlySimple = true};
            c.AddPath(poly, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, result, fillType, fillType);
            return result;
        }

        //------------------------------------------------------------------------------

        public static Paths SimplifyPolygons(Paths polys,
            PolyFillType fillType = PolyFillType.pftEvenOdd)
        {
            var result = new Paths();
            var c = new AngusjClipper {StrictlySimple = true};
            c.AddPaths(polys, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, result, fillType, fillType);
            return result;
        }

        //------------------------------------------------------------------------------

        private static float DistanceFromLineSqrd(Vector2 pt, Vector2 ln1, Vector2 ln2)
        {
            //The equation of a line in general form (Ax + By + C = 0)
            //given 2 points (x¹,y¹) & (x²,y²) is ...
            //(y¹ - y²)x + (x² - x¹)y + (y² - y¹)x¹ - (x² - x¹)y¹ = 0
            //A = (y¹ - y²); B = (x² - x¹); C = (y² - y¹)x¹ - (x² - x¹)y¹
            //perpendicular distance of point (x³,y³) = (Ax³ + By³ + C)/Sqrt(A² + B²)
            //see http://en.wikipedia.org/wiki/Perpendicular_distance
            var A = ln1.Y - ln2.Y;
            var B = ln2.X - ln1.X;
            var C = A*ln1.X + B*ln1.Y;
            C = A*pt.X + B*pt.Y - C;
            return (C*C)/(A*A + B*B);
        }

        //---------------------------------------------------------------------------

        private static bool SlopesNearCollinear(Vector2 pt1,
            Vector2 pt2, Vector2 pt3, float distSqrd)
        {
            //this function is more accurate when the point that's GEOMETRICALLY 
            //between the other 2 points is the one that's tested for distance.  
            //nb: with 'spikes', either pt1 or pt3 is geometrically between the other pts                    
            if (Math.Abs(pt1.X - pt2.X) > Math.Abs(pt1.Y - pt2.Y))
            {
                if ((pt1.X > pt2.X) == (pt1.X < pt3.X))
                    return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
                if ((pt2.X > pt1.X) == (pt2.X < pt3.X))
                    return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
                return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
            }
            if ((pt1.Y > pt2.Y) == (pt1.Y < pt3.Y))
                return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
            if ((pt2.Y > pt1.Y) == (pt2.Y < pt3.Y))
                return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
            return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
        }

        //------------------------------------------------------------------------------

        private static bool PointsAreClose(Vector2 pt1, Vector2 pt2, float distSqrd)
        {
            var dx = pt1.X - pt2.X;
            var dy = pt1.Y - pt2.Y;
            return ((dx*dx) + (dy*dy) <= distSqrd);
        }

        //------------------------------------------------------------------------------

        private static OutPt ExcludeOp(OutPt op)
        {
            var result = op.Prev;
            result.Next = op.Next;
            op.Next.Prev = result;
            result.Idx = 0;
            return result;
        }

        //------------------------------------------------------------------------------

        public static Path CleanPolygon(Path path, float distance = 1.415f)
        {
            //distance = proximity in units/pixels below which vertices will be stripped. 
            //Default ~= sqrt(2) so when adjacent vertices or semi-adjacent vertices have 
            //both x & y coords within 1 unit, then the second vertex will be stripped.

            var cnt = path.Count;

            if (cnt == 0) return new Path();

            var outPts = new OutPt[cnt];
            for (var i = 0; i < cnt; ++i) outPts[i] = new OutPt();

            for (var i = 0; i < cnt; ++i)
            {
                outPts[i].Pt = path[i];
                outPts[i].Next = outPts[(i + 1)%cnt];
                outPts[i].Next.Prev = outPts[i];
                outPts[i].Idx = 0;
            }

            var distSqrd = distance*distance;
            var op = outPts[0];
            while (op.Idx == 0 && op.Next != op.Prev)
            {
                if (PointsAreClose(op.Pt, op.Prev.Pt, distSqrd))
                {
                    op = ExcludeOp(op);
                    cnt--;
                }
                else if (PointsAreClose(op.Prev.Pt, op.Next.Pt, distSqrd))
                {
                    ExcludeOp(op.Next);
                    op = ExcludeOp(op);
                    cnt -= 2;
                }
                else if (SlopesNearCollinear(op.Prev.Pt, op.Pt, op.Next.Pt, distSqrd))
                {
                    op = ExcludeOp(op);
                    cnt--;
                }
                else
                {
                    op.Idx = 1;
                    op = op.Next;
                }
            }

            if (cnt < 3) cnt = 0;
            var result = new Path(cnt);
            for (var i = 0; i < cnt; ++i)
            {
                result.Add(op.Pt);
                op = op.Next;
            }
            return result;
        }

        //------------------------------------------------------------------------------

        public static Paths CleanPolygons(Paths polys,
            float distance = 1.415f)
        {
            var result = new Paths(polys.Count);
            for (var i = 0; i < polys.Count; i++)
                result.Add(CleanPolygon(polys[i], distance));
            return result;
        }

        //------------------------------------------------------------------------------

        internal static Paths Minkowski(Path pattern, Path path, bool IsSum, bool IsClosed)
        {
            var delta = (IsClosed ? 1 : 0);
            var polyCnt = pattern.Count;
            var pathCnt = path.Count;
            var result = new Paths(pathCnt);
            if (IsSum)
                for (var i = 0; i < pathCnt; i++)
                {
                    var p = new Path(polyCnt);
                    foreach (var ip in pattern)
                        p.Add(new Vector2(path[i].X + ip.X, path[i].Y + ip.Y));
                    result.Add(p);
                }
            else
                for (var i = 0; i < pathCnt; i++)
                {
                    var p = new Path(polyCnt);
                    foreach (var ip in pattern)
                        p.Add(new Vector2(path[i].X - ip.X, path[i].Y - ip.Y));
                    result.Add(p);
                }

            var quads = new Paths((pathCnt + delta)*(polyCnt + 1));
            for (var i = 0; i < pathCnt - 1 + delta; i++)
                for (var j = 0; j < polyCnt; j++)
                {
                    var quad = new Path(4)
                    {
                        result[i%pathCnt][j%polyCnt],
                        result[(i + 1)%pathCnt][j%polyCnt],
                        result[(i + 1)%pathCnt][(j + 1)%polyCnt],
                        result[i%pathCnt][(j + 1)%polyCnt]
                    };
                    if (!Orientation(quad)) quad.Reverse();
                    quads.Add(quad);
                }
            return quads;
        }

        //------------------------------------------------------------------------------

        public static Paths MinkowskiSum(Path pattern, Path path, bool pathIsClosed)
        {
            var paths = Minkowski(pattern, path, true, pathIsClosed);
            var c = new AngusjClipper();
            c.AddPaths(paths, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, paths, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            return paths;
        }

        //------------------------------------------------------------------------------

        private static Path TranslatePath(Path path, Vector2 delta)
        {
            var outPath = new Path(path.Count);
            for (var i = 0; i < path.Count; i++)
                outPath.Add(new Vector2(path[i].X + delta.X, path[i].Y + delta.Y));
            return outPath;
        }

        //------------------------------------------------------------------------------

        public static Paths MinkowskiSum(Path pattern, Paths paths, bool pathIsClosed)
        {
            var solution = new Paths();
            var c = new AngusjClipper();
            for (var i = 0; i < paths.Count; ++i)
            {
                var tmp = Minkowski(pattern, paths[i], true, pathIsClosed);
                c.AddPaths(tmp, PolyType.ptSubject, true);
                if (pathIsClosed)
                {
                    var path = TranslatePath(paths[i], pattern[0]);
                    c.AddPath(path, PolyType.ptClip, true);
                }
            }
            c.Execute(ClipType.ctUnion, solution,
                PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            return solution;
        }

        //------------------------------------------------------------------------------

        public static Paths MinkowskiDiff(Path poly1, Path poly2)
        {
            var paths = Minkowski(poly1, poly2, false, true);
            var c = new AngusjClipper();
            c.AddPaths(paths, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, paths, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            return paths;
        }

        //------------------------------------------------------------------------------

        internal enum NodeType
        {
            ntAny,
            ntOpen,
            ntClosed
        };

        public static Paths PolyTreeToPaths(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.Total};
            AddPolyNodeToPaths(polytree, NodeType.ntAny, result);
            return result;
        }

        //------------------------------------------------------------------------------

        internal static void AddPolyNodeToPaths(PolyNode polynode, NodeType nt, Paths paths)
        {
            var match = true;
            switch (nt)
            {
                case NodeType.ntOpen:
                    return;
                case NodeType.ntClosed:
                    match = !polynode.IsOpen;
                    break;
            }

            if (polynode.m_polygon.Count > 0 && match)
                paths.Add(polynode.m_polygon);
            foreach (var pn in polynode.Childs)
                AddPolyNodeToPaths(pn, nt, paths);
        }

        //------------------------------------------------------------------------------

        public static Paths OpenPathsFromPolyTree(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.ChildCount};
            for (var i = 0; i < polytree.ChildCount; i++)
                if (polytree.Childs[i].IsOpen)
                    result.Add(polytree.Childs[i].m_polygon);
            return result;
        }

        //------------------------------------------------------------------------------

        public static Paths ClosedPathsFromPolyTree(PolyTree polytree)
        {
            var result = new Paths {Capacity = polytree.Total};
            AddPolyNodeToPaths(polytree, NodeType.ntClosed, result);
            return result;
        }

        //------------------------------------------------------------------------------
    } //end Clipper

    internal class ClipperOffset
    {
        private Paths m_destPolys;
        private Path m_srcPoly;
        private Path m_destPoly;
        private readonly List<Vector2> m_normals = new List<Vector2>();
        private float m_delta, m_sinA, m_sin, m_cos;
        private float m_miterLim, m_StepsPerRad;

        private Vector2 m_lowest;
        private readonly PolyNode m_polyNodes = new PolyNode();

        public float ArcTolerance { get; set; }
        public float MiterLimit { get; set; }

        private const float two_pi = (float) Math.PI*2f;
        private const float def_arc_tolerance = 0.25f;

        public ClipperOffset(
            float miterLimit = 2.0f, float arcTolerance = def_arc_tolerance)
        {
            MiterLimit = miterLimit;
            ArcTolerance = arcTolerance;
            m_lowest.X = -1;
        }

        //------------------------------------------------------------------------------

        public void Clear()
        {
            m_polyNodes.Childs.Clear();
            m_lowest.X = -1;
        }

        //------------------------------------------------------------------------------

        internal static int Round(float value)
        {
            return value < 0 ? (int) (value - 0.5) : (int) (value + 0.5);
        }

        //------------------------------------------------------------------------------

        public void AddPath(Path path, JoinType joinType, EndType endType)
        {
            var highI = path.Count - 1;
            if (highI < 0) return;
            var newNode = new PolyNode
            {
                m_jointype = joinType,
                m_endtype = endType
            };

            //strip duplicate points from path and also get index to the lowest point ...
            if (endType == EndType.etClosedLine || endType == EndType.etClosedPolygon)
                while (highI > 0 && path[0] == path[highI]) highI--;
            newNode.m_polygon.Capacity = highI + 1;
            newNode.m_polygon.Add(path[0]);
            int j = 0, k = 0;
            for (var i = 1; i <= highI; i++)
                if (newNode.m_polygon[j] != path[i])
                {
                    j++;
                    newNode.m_polygon.Add(path[i]);
                    if (path[i].Y > newNode.m_polygon[k].Y ||
                        (Calc.NearEqual(path[i].Y, newNode.m_polygon[k].Y) &&
                         path[i].X < newNode.m_polygon[k].X)) k = j;
                }
            if (endType == EndType.etClosedPolygon && j < 2) return;

            m_polyNodes.AddChild(newNode);

            //if this path's lowest pt is lower than all the others then update m_lowest
            if (endType != EndType.etClosedPolygon) return;
            if (m_lowest.X < 0)
                m_lowest = new Vector2(m_polyNodes.ChildCount - 1, k);
            else
            {
                Vector2 ip = m_polyNodes.Childs[(int) m_lowest.X].m_polygon[(int) m_lowest.Y];
                if (newNode.m_polygon[k].Y > ip.Y ||
                    (Calc.NearEqual(newNode.m_polygon[k].Y, ip.Y) &&
                     newNode.m_polygon[k].X < ip.X))
                    m_lowest = new Vector2(m_polyNodes.ChildCount - 1, k);
            }
        }

        //------------------------------------------------------------------------------

        public void AddPaths(Paths paths, JoinType joinType, EndType endType)
        {
            foreach (var p in paths)
                AddPath(p, joinType, endType);
        }

        //------------------------------------------------------------------------------

        private void FixOrientations()
        {
            //fixup orientations of all closed paths if the orientation of the
            //closed path with the lowermost vertex is wrong ...
            if (m_lowest.X >= 0 &&
                !AngusjClipper.Orientation(m_polyNodes.Childs[(int) m_lowest.X].m_polygon))
            {
                for (var i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    var node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedPolygon ||
                        (node.m_endtype == EndType.etClosedLine &&
                         AngusjClipper.Orientation(node.m_polygon)))
                        node.m_polygon.Reverse();
                }
            }
            else
            {
                for (var i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    var node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedLine &&
                        !AngusjClipper.Orientation(node.m_polygon))
                        node.m_polygon.Reverse();
                }
            }
        }

        //------------------------------------------------------------------------------

        internal static Vector2 GetUnitNormal(Vector2 pt1, Vector2 pt2)
        {
            var dx = (pt2.X - pt1.X);
            var dy = (pt2.Y - pt1.Y);
            if (Calc.NearZero(dx) && Calc.NearZero(dy)) return new Vector2();

            var f = 1f*1.0f/(float) Math.Sqrt(dx*dx + dy*dy);
            dx *= f;
            dy *= f;

            return new Vector2(dy, -dx);
        }

        //------------------------------------------------------------------------------

        private void DoOffset(float delta)
        {
            m_destPolys = new Paths();
            m_delta = delta;

            //if Zero offset, just copy any CLOSED polygons to m_p and return ...
            if (ClipperBase.NearZero(delta))
            {
                m_destPolys.Capacity = m_polyNodes.ChildCount;
                for (var i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    var node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedPolygon)
                        m_destPolys.Add(node.m_polygon);
                }
                return;
            }

            //see offset_triginometry3.svg in the documentation folder ...
            if (MiterLimit > 2) m_miterLim = 2/(MiterLimit*MiterLimit);
            else m_miterLim = 0.5f;

            float y;
            if (ArcTolerance <= 0.0)
                y = def_arc_tolerance;
            else if (ArcTolerance > Math.Abs(delta)*def_arc_tolerance)
                y = Math.Abs(delta)*def_arc_tolerance;
            else
                y = ArcTolerance;
            //see offset_triginometry2.svg in the documentation folder ...
            var steps = (float) Math.PI/(float) Math.Acos(1 - y/Math.Abs(delta));
            m_sin = (float) Math.Sin(two_pi/steps);
            m_cos = (float) Math.Cos(two_pi/steps);
            m_StepsPerRad = steps/two_pi;
            if (delta < 0.0) m_sin = -m_sin;

            m_destPolys.Capacity = m_polyNodes.ChildCount*2;
            for (var i = 0; i < m_polyNodes.ChildCount; i++)
            {
                var node = m_polyNodes.Childs[i];
                m_srcPoly = node.m_polygon;

                var len = m_srcPoly.Count;

                if (len == 0 || (delta <= 0 && (len < 3 ||
                                                node.m_endtype != EndType.etClosedPolygon)))
                    continue;

                m_destPoly = new Path();

                if (len == 1)
                {
                    if (node.m_jointype == JoinType.jtRound)
                    {
                        float X = 1.0f, Y = 0.0f;
                        for (var j = 1; j <= steps; j++)
                        {
                            m_destPoly.Add(new Vector2(
                                Round(m_srcPoly[0].X + X*delta),
                                Round(m_srcPoly[0].Y + Y*delta)));
                            var X2 = X;
                            X = X*m_cos - m_sin*Y;
                            Y = X2*m_sin + Y*m_cos;
                        }
                    }
                    else
                    {
                        float X = -1.0f, Y = -1.0f;
                        for (var j = 0; j < 4; ++j)
                        {
                            m_destPoly.Add(new Vector2(
                                Round(m_srcPoly[0].X + X*delta),
                                Round(m_srcPoly[0].Y + Y*delta)));
                            if (X < 0) X = 1;
                            else if (Y < 0) Y = 1;
                            else X = -1;
                        }
                    }
                    m_destPolys.Add(m_destPoly);
                    continue;
                }

                //build m_normals ...
                m_normals.Clear();
                m_normals.Capacity = len;
                for (var j = 0; j < len - 1; j++)
                    m_normals.Add(GetUnitNormal(m_srcPoly[j], m_srcPoly[j + 1]));
                if (node.m_endtype == EndType.etClosedLine ||
                    node.m_endtype == EndType.etClosedPolygon)
                    m_normals.Add(GetUnitNormal(m_srcPoly[len - 1], m_srcPoly[0]));
                else
                    m_normals.Add(m_normals[len - 2]);

                if (node.m_endtype == EndType.etClosedPolygon)
                {
                    var k = len - 1;
                    for (var j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                }
                else if (node.m_endtype == EndType.etClosedLine)
                {
                    var k = len - 1;
                    for (var j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                    m_destPoly = new Path();
                    //re-build m_normals ...
                    var n = m_normals[len - 1];
                    for (var j = len - 1; j > 0; j--)
                        m_normals[j] = new Vector2(-m_normals[j - 1].X, -m_normals[j - 1].Y);
                    m_normals[0] = new Vector2(-n.X, -n.Y);
                    k = 0;
                    for (var j = len - 1; j >= 0; j--)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                }
                else
                {
                    var k = 0;
                    for (var j = 1; j < len - 1; ++j)
                        OffsetPoint(j, ref k, node.m_jointype);

                    Vector2 pt1;
                    if (node.m_endtype == EndType.etOpenButt)
                    {
                        var j = len - 1;
                        pt1 = new Vector2(Round(m_srcPoly[j].X + m_normals[j].X*
                                                delta), Round(m_srcPoly[j].Y + m_normals[j].Y*delta));
                        m_destPoly.Add(pt1);
                        pt1 = new Vector2(Round(m_srcPoly[j].X - m_normals[j].X*
                                                delta), Round(m_srcPoly[j].Y - m_normals[j].Y*delta));
                        m_destPoly.Add(pt1);
                    }
                    else
                    {
                        var j = len - 1;
                        k = len - 2;
                        m_sinA = 0;
                        m_normals[j] = new Vector2(-m_normals[j].X, -m_normals[j].Y);
                        if (node.m_endtype == EndType.etOpenSquare)
                            DoSquare(j, k);
                        else
                            DoRound(j, k);
                    }

                    //re-build m_normals ...
                    for (var j = len - 1; j > 0; j--)
                        m_normals[j] = new Vector2(-m_normals[j - 1].X, -m_normals[j - 1].Y);

                    m_normals[0] = new Vector2(-m_normals[1].X, -m_normals[1].Y);

                    k = len - 1;
                    for (var j = k - 1; j > 0; --j)
                        OffsetPoint(j, ref k, node.m_jointype);

                    if (node.m_endtype == EndType.etOpenButt)
                    {
                        pt1 = new Vector2(Round(m_srcPoly[0].X - m_normals[0].X*delta),
                            Round(m_srcPoly[0].Y - m_normals[0].Y*delta));
                        m_destPoly.Add(pt1);
                        pt1 = new Vector2(Round(m_srcPoly[0].X + m_normals[0].X*delta),
                            Round(m_srcPoly[0].Y + m_normals[0].Y*delta));
                        m_destPoly.Add(pt1);
                    }
                    else
                    {
                        m_sinA = 0;
                        if (node.m_endtype == EndType.etOpenSquare)
                            DoSquare(0, 1);
                        else
                            DoRound(0, 1);
                    }
                    m_destPolys.Add(m_destPoly);
                }
            }
        }

        //------------------------------------------------------------------------------

        public void Execute(ref Paths solution, float delta)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);
            //now clean up 'corners' ...
            var clpr = new AngusjClipper();
            clpr.AddPaths(m_destPolys, PolyType.ptSubject, true);
            if (delta > 0)
            {
                clpr.Execute(ClipType.ctUnion, solution,
                    PolyFillType.pftPositive, PolyFillType.pftPositive);
            }
            else
            {
                var r = ClipperBase.GetBounds(m_destPolys);
                var outer = new Path(4)
                {
                    new Vector2(r.Left - 10, r.Bottom + 10),
                    new Vector2(r.Right + 10, r.Bottom + 10),
                    new Vector2(r.Right + 10, r.Top - 10),
                    new Vector2(r.Left - 10, r.Top - 10)
                };


                clpr.AddPath(outer, PolyType.ptSubject, true);
                clpr.ReverseSolution = true;
                clpr.Execute(ClipType.ctUnion, solution, PolyFillType.pftNegative, PolyFillType.pftNegative);
                if (solution.Count > 0) solution.RemoveAt(0);
            }
        }

        //------------------------------------------------------------------------------

        public void Execute(ref PolyTree solution, float delta)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);

            //now clean up 'corners' ...
            var clpr = new AngusjClipper();
            clpr.AddPaths(m_destPolys, PolyType.ptSubject, true);
            if (delta > 0)
            {
                clpr.Execute(ClipType.ctUnion, solution,
                    PolyFillType.pftPositive, PolyFillType.pftPositive);
            }
            else
            {
                var r = ClipperBase.GetBounds(m_destPolys);
                var outer = new Path(4)
                {
                    new Vector2(r.Left - 10, r.Bottom + 10),
                    new Vector2(r.Right + 10, r.Bottom + 10),
                    new Vector2(r.Right + 10, r.Top - 10),
                    new Vector2(r.Left - 10, r.Top - 10)
                };


                clpr.AddPath(outer, PolyType.ptSubject, true);
                clpr.ReverseSolution = true;
                clpr.Execute(ClipType.ctUnion, solution, PolyFillType.pftNegative, PolyFillType.pftNegative);
                //remove the outer PolyNode rectangle ...
                if (solution.ChildCount == 1 && solution.Childs[0].ChildCount > 0)
                {
                    var outerNode = solution.Childs[0];
                    solution.Childs.Capacity = outerNode.ChildCount;
                    solution.Childs[0] = outerNode.Childs[0];
                    solution.Childs[0].m_Parent = solution;
                    for (var i = 1; i < outerNode.ChildCount; i++)
                        solution.AddChild(outerNode.Childs[i]);
                }
                else
                    solution.Clear();
            }
        }

        //------------------------------------------------------------------------------

        private void OffsetPoint(int j, ref int k, JoinType jointype)
        {
            //cross product ...
            m_sinA = (m_normals[k].X*m_normals[j].Y - m_normals[j].X*m_normals[k].Y);

            if (Math.Abs(m_sinA*m_delta) < 1.0)
            {
                //dot product ...
                var cosA = (m_normals[k].X*m_normals[j].X + m_normals[j].Y*m_normals[k].Y);
                if (cosA > 0) // angle ==> 0 degrees
                {
                    m_destPoly.Add(new Vector2(Round(m_srcPoly[j].X + m_normals[k].X*m_delta),
                        Round(m_srcPoly[j].Y + m_normals[k].Y*m_delta)));
                    return;
                }
                //else angle ==> 180 degrees   
            }
            else if (m_sinA > 1.0) m_sinA = 1.0f;
            else if (m_sinA < -1.0) m_sinA = -1.0f;

            if (m_sinA*m_delta < 0)
            {
                m_destPoly.Add(new Vector2(Round(m_srcPoly[j].X + m_normals[k].X*m_delta),
                    Round(m_srcPoly[j].Y + m_normals[k].Y*m_delta)));
                m_destPoly.Add(m_srcPoly[j]);
                m_destPoly.Add(new Vector2(Round(m_srcPoly[j].X + m_normals[j].X*m_delta),
                    Round(m_srcPoly[j].Y + m_normals[j].Y*m_delta)));
            }
            else
                switch (jointype)
                {
                    case JoinType.jtMiter:
                    {
                        var r = 1 + (m_normals[j].X*m_normals[k].X +
                                     m_normals[j].Y*m_normals[k].Y);
                        if (r >= m_miterLim) DoMiter(j, k, r);
                        else DoSquare(j, k);
                        break;
                    }
                    case JoinType.jtSquare:
                        DoSquare(j, k);
                        break;
                    case JoinType.jtRound:
                        DoRound(j, k);
                        break;
                }
            k = j;
        }

        //------------------------------------------------------------------------------

        internal void DoSquare(int j, int k)
        {
            var dx = (float) Math.Tan(Math.Atan2(m_sinA,
                m_normals[k].X*m_normals[j].X + m_normals[k].Y*m_normals[j].Y)/4);
            m_destPoly.Add(new Vector2(
                Round(m_srcPoly[j].X + m_delta*(m_normals[k].X - m_normals[k].Y*dx)),
                Round(m_srcPoly[j].Y + m_delta*(m_normals[k].Y + m_normals[k].X*dx))));
            m_destPoly.Add(new Vector2(
                Round(m_srcPoly[j].X + m_delta*(m_normals[j].X + m_normals[j].Y*dx)),
                Round(m_srcPoly[j].Y + m_delta*(m_normals[j].Y - m_normals[j].X*dx))));
        }

        //------------------------------------------------------------------------------

        internal void DoMiter(int j, int k, float r)
        {
            var q = m_delta/r;
            m_destPoly.Add(new Vector2(Round(m_srcPoly[j].X + (m_normals[k].X + m_normals[j].X)*q),
                Round(m_srcPoly[j].Y + (m_normals[k].Y + m_normals[j].Y)*q)));
        }

        //------------------------------------------------------------------------------

        internal void DoRound(int j, int k)
        {
            var a = (float) Math.Atan2(m_sinA,
                m_normals[k].X*m_normals[j].X + m_normals[k].Y*m_normals[j].Y);
            var steps = Math.Max(Round(m_StepsPerRad*Math.Abs(a)), 1);

            float X = m_normals[k].X, Y = m_normals[k].Y;
            for (var i = 0; i < steps; ++i)
            {
                m_destPoly.Add(new Vector2(
                    Round(m_srcPoly[j].X + X*m_delta),
                    Round(m_srcPoly[j].Y + Y*m_delta)));
                float X2 = X;
                X = X*m_cos - m_sin*Y;
                Y = X2*m_sin + Y*m_cos;
            }
            m_destPoly.Add(new Vector2(
                Round(m_srcPoly[j].X + m_normals[j].X*m_delta),
                Round(m_srcPoly[j].Y + m_normals[j].Y*m_delta)));
        }

        //------------------------------------------------------------------------------
    }

    internal class ClipperException : Exception
    {
        public ClipperException(string description) : base(description)
        {
        }
    }

    //------------------------------------------------------------------------------
}