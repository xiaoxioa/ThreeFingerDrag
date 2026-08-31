using System;
using System.Collections.Generic;

namespace ThreeFingerDrag
{
    internal struct ContactPoint
    {
        internal readonly int Id;
        internal readonly double X;
        internal readonly double Y;

        internal ContactPoint(int id, double x, double y)
        {
            Id = id;
            X = x;
            Y = y;
        }
    }

    internal interface IDragOutput
    {
        void ButtonDown();
        void Move(double normalizedX, double normalizedY);
        void ButtonUp();
    }

    internal sealed class GestureEngine
    {
        private enum GestureState { Idle, Armed, Dragging, Grace }

        private readonly IDragOutput output;
        private GestureState state;
        private double anchorX;
        private double anchorY;
        private double previousX;
        private double previousY;
        private string contactKey;
        private long releaseAt;

        internal bool Enabled = true;
        internal int GraceMilliseconds = 350;
        internal double ActivationDistance = 0.0035;
        internal bool IsDragging { get { return state == GestureState.Dragging || state == GestureState.Grace; } }
        internal bool IsInGrace { get { return state == GestureState.Grace; } }
        internal event Action GraceStarted;

        internal GestureEngine(IDragOutput output)
        {
            this.output = output;
        }

        internal void Update(IList<ContactPoint> contacts, long nowMilliseconds)
        {
            if (!Enabled)
            {
                Cancel();
                return;
            }

            if (contacts.Count == 3)
            {
                UpdateThreeContacts(contacts);
                return;
            }

            if (state == GestureState.Armed)
            {
                state = GestureState.Idle;
                contactKey = null;
            }
            else if (state == GestureState.Dragging)
            {
                if (contacts.Count < 3 && GraceMilliseconds > 0)
                {
                    state = GestureState.Grace;
                    releaseAt = nowMilliseconds + GraceMilliseconds;
                    if (GraceStarted != null) GraceStarted();
                }
                else
                {
                    Release();
                }
            }
            else if (state == GestureState.Grace && contacts.Count > 3)
            {
                Release();
            }
        }

        internal void Tick(long nowMilliseconds)
        {
            if (state == GestureState.Grace && nowMilliseconds >= releaseAt)
                Release();
        }

        internal void Cancel()
        {
            if (IsDragging)
                output.ButtonUp();
            state = GestureState.Idle;
            contactKey = null;
            releaseAt = 0;
        }

        private void UpdateThreeContacts(IList<ContactPoint> contacts)
        {
            double x = 0;
            double y = 0;
            int[] ids = new int[3];
            for (int i = 0; i < 3; i++)
            {
                x += contacts[i].X;
                y += contacts[i].Y;
                ids[i] = contacts[i].Id;
            }
            x /= 3.0;
            y /= 3.0;
            Array.Sort(ids);
            string key = ids[0] + ":" + ids[1] + ":" + ids[2];

            if (state == GestureState.Idle)
            {
                Arm(x, y, key);
                return;
            }
            if (state == GestureState.Grace)
            {
                state = GestureState.Dragging;
                previousX = x;
                previousY = y;
                contactKey = key;
                releaseAt = 0;
                return;
            }
            if (contactKey != key)
            {
                // 换指时重新定基准，防止光标瞬移。
                anchorX = previousX = x;
                anchorY = previousY = y;
                contactKey = key;
                return;
            }

            if (state == GestureState.Armed)
            {
                double dx = x - anchorX;
                double dy = y - anchorY;
                if (Math.Sqrt(dx * dx + dy * dy) >= ActivationDistance)
                {
                    output.ButtonDown();
                    state = GestureState.Dragging;
                    output.Move(dx, dy);
                    previousX = x;
                    previousY = y;
                }
                return;
            }

            if (state == GestureState.Dragging)
            {
                double dx = x - previousX;
                double dy = y - previousY;
                if (dx != 0 || dy != 0)
                    output.Move(dx, dy);
                previousX = x;
                previousY = y;
            }
        }

        private void Arm(double x, double y, string key)
        {
            state = GestureState.Armed;
            anchorX = previousX = x;
            anchorY = previousY = y;
            contactKey = key;
        }

        private void Release()
        {
            output.ButtonUp();
            state = GestureState.Idle;
            contactKey = null;
            releaseAt = 0;
        }
    }
}
