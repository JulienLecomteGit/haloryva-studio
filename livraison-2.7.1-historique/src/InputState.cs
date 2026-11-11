using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CursorFlow
{
    // Présentation uniquement : le hook, les appuis longs, le rejeu Windows et
    // les profils conservent l'identité native X1/X2 sans permutation cachée.
    internal static class SideButtonMap
    {
        internal static int NativeIndex(int displayedIndex, bool inverted)
        {
            if (!inverted) return displayedIndex;
            return displayedIndex == 3 ? 4 : displayedIndex == 4 ? 3 : displayedIndex;
        }
        internal static string ToDisplay(string nativeButton, bool inverted)
        {
            if (!inverted) return nativeButton;
            return nativeButton == "X1" ? "X2" : nativeButton == "X2" ? "X1" : nativeButton;
        }
        internal static string ReadAction(Config options, int displayedIndex, bool longPress)
        {
            switch (NativeIndex(displayedIndex, options.SwapSideButtons))
            {
                case 0: return longPress ? options.ButtonLeftLong : options.ButtonLeft;
                case 1: return longPress ? options.ButtonRightLong : options.ButtonRight;
                case 2: return longPress ? options.ButtonMiddleLong : options.ButtonMiddle;
                case 3: return longPress ? options.ButtonX1Long : options.ButtonX1;
                case 4: return longPress ? options.ButtonX2Long : options.ButtonX2;
                default: throw new ArgumentOutOfRangeException("displayedIndex");
            }
        }
        internal static void WriteAction(Config options, int displayedIndex, string action, bool longPress)
        {
            switch (NativeIndex(displayedIndex, options.SwapSideButtons))
            {
                case 0: if (longPress) options.ButtonLeftLong = action; else options.ButtonLeft = action; break;
                case 1: if (longPress) options.ButtonRightLong = action; else options.ButtonRight = action; break;
                case 2: if (longPress) options.ButtonMiddleLong = action; else options.ButtonMiddle = action; break;
                case 3: if (longPress) options.ButtonX1Long = action; else options.ButtonX1 = action; break;
                case 4: if (longPress) options.ButtonX2Long = action; else options.ButtonX2 = action; break;
                default: throw new ArgumentOutOfRangeException("displayedIndex");
            }
        }
    }

    // Production logic deliberately independent of HWNDs. Tested by ProtocolTests.cs.
    internal enum MouseRoute { Pass, Block, Held, Drawing }
    internal sealed class RoutedPress
    {
        internal readonly MouseRoute Route;
        internal readonly int DrawingSession;
        internal RoutedPress(MouseRoute route, int session) { Route = route; DrawingSession = session; }
    }
    internal sealed class MouseRoutes
    {
        private readonly Dictionary<string, RoutedPress> routes = new Dictionary<string, RoutedPress>();
        internal void Begin(string button, MouseRoute route, int session)
        { routes[button] = new RoutedPress(route, session); }
        internal RoutedPress Release(string button)
        {
            RoutedPress found;
            if (!routes.TryGetValue(button, out found)) return new RoutedPress(MouseRoute.Pass, -1);
            routes.Remove(button); return found;
        }
        internal bool IsDrawing(string button, int session)
        { RoutedPress p; return routes.TryGetValue(button, out p) && p.Route == MouseRoute.Drawing && p.DrawingSession == session; }
    }
    internal sealed class PressCommand
    {
        internal string Button, Action;
        internal int X, Y;
        internal bool Long;
    }
    internal sealed class PressBook
    {
        private sealed class Pending
        {
            internal long Started;
            internal int Threshold, X, Y;
            internal string ShortAction, LongAction;
            internal bool Done;
        }
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private readonly Func<long> clock;
        internal PressBook() : this(CreateClock()) { }
        internal PressBook(Func<long> clock) { this.clock = clock; }
        private static Func<long> CreateClock()
        { Stopwatch watch = Stopwatch.StartNew(); return delegate { return watch.ElapsedMilliseconds; }; }
        internal void Clear() { pending.Clear(); }
        internal bool Begin(string button, string shortAction, string longAction, int threshold, int x, int y)
        {
            if (button != "Milieu" && button != "X1" && button != "X2") return false;
            if (String.IsNullOrEmpty(longAction) || longAction == "Normal") return false;
            if (!pending.ContainsKey(button)) pending.Add(button, new Pending {
                Started = clock(), Threshold = Math.Max(250, Math.Min(1200, threshold)),
                X = x, Y = y, ShortAction = shortAction ?? "Normal", LongAction = longAction });
            return true;
        }
        private PressCommand Command(string button, Pending p, bool longPress)
        { return new PressCommand { Button = button, Action = longPress ? p.LongAction : p.ShortAction, X = p.X, Y = p.Y, Long = longPress }; }
        internal PressCommand Release(string button)
        {
            Pending p;
            if (!pending.TryGetValue(button, out p)) return null;
            pending.Remove(button); // remove BEFORE a possibly reentrant action
            return p.Done ? null : Command(button, p, clock() - p.Started >= p.Threshold);
        }
        internal void Poll(Action<PressCommand> execute)
        {
            foreach (string button in new List<string>(pending.Keys))
            {
                Pending p;
                if (!pending.TryGetValue(button, out p)) continue;
                long elapsed = clock() - p.Started;
                if (!p.Done && elapsed >= p.Threshold)
                {
                    p.Done = true; // commit BEFORE callback: pause/reset may clear pending
                    execute(Command(button, p, true));
                }
                else if (elapsed > 20000) pending.Remove(button);
            }
        }
    }
}
