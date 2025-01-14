﻿using OpenTK;

namespace PSXPrev.Common
{
    // Hierarchical coordinates for use with HMD models.
    // This is needed so that models can be treated as nested,
    // even when they're stored in a flat list of the root entity.
    public class Coordinate
    {
        public const uint NoID = uint.MaxValue;

        private Matrix4 _originalLocalMatrix;
        private Vector3 _originalTranslation;
        private Vector3 _originalRotation;

        private Matrix4 _localMatrix;
        public Matrix4 LocalMatrix
        {
            get => _localMatrix;
            set => _localMatrix = value;
        }
        public Matrix4 OriginalLocalMatrix
        {
            get => _originalLocalMatrix;
            set => LocalMatrix = _originalLocalMatrix = value;
        }

        public Vector3 Translation { get; set; }
        public Vector3 OriginalTranslation
        {
            get => _originalTranslation;
            set => Translation = _originalTranslation = value;
        }

        public Vector3 Rotation { get; set; }
        public Vector3 OriginalRotation
        {
            get => _originalRotation;
            set => Rotation = _originalRotation = value;
        }

        public RotationOrder RotationOrder { get; set; }
        public RotationOrder OriginalRotationOrder => RotationOrder.YXZ; // Observed in Gods98 (PSX) source.
        
        public uint ID { get; set; } = NoID;
        public uint ParentID { get; set; } = NoID;
        public bool IsAbsolute { get; set; } // True if parents do not affect WorldMatrix

        public Coordinate[] Coords { get; set; }

        public uint TMDID => ID + 1;

        public bool HasParent => ParentID != NoID && ParentID != ID;
        public bool HasTransformParent => HasParent && !IsAbsolute;
        public Coordinate Parent => (HasParent ? Coords?[ParentID] : null);
        
        public Matrix4 WorldMatrix
        {
            get
            {
                var matrix = _localMatrix;
                if (!IsAbsolute)
                {
                    var coord = Parent;
                    while (coord != null)
                    {
                        Matrix4.Mult(ref matrix, ref coord._localMatrix, out matrix);
                        coord = coord.Parent;
                    }
                }
                return matrix;
            }
        }

        public Coordinate()
        {
            OriginalLocalMatrix = Matrix4.Identity;
            // Everything below is unused at the moment:
            OriginalTranslation = Vector3.Zero;
            OriginalRotation = Vector3.Zero;
            RotationOrder = OriginalRotationOrder;
        }

        public Coordinate(Coordinate fromCoordinate, Coordinate[] coords)
        {
            _originalLocalMatrix = fromCoordinate._originalLocalMatrix;
            _originalTranslation = fromCoordinate._originalTranslation;
            _originalRotation = fromCoordinate._originalRotation;

            LocalMatrix = fromCoordinate.LocalMatrix;
            Translation = fromCoordinate.Translation;
            Rotation = fromCoordinate.Rotation;
            RotationOrder = fromCoordinate.RotationOrder;

            ID = fromCoordinate.ID;
            ParentID = fromCoordinate.ParentID;
            IsAbsolute = fromCoordinate.IsAbsolute;

            Coords = coords;
        }

        public void ResetTransform()
        {
            LocalMatrix = OriginalLocalMatrix;
            // Everything below is unused at the moment:
            Translation = OriginalTranslation;
            Rotation = OriginalRotation;
            RotationOrder = OriginalRotationOrder;
        }
        
        // Check to see if any coord units have parents that eventually reference themselves.
        public static bool FindCircularReferences(Coordinate[] coords)
        {
            // Optimized circular reference detection so we only need to visit N coords, instead of N^2 coords.
            var visitedCoords = new uint[coords.Length];
            foreach (var coord in coords)
            {
                // Used to compare visitedCoords to see if we've already visited this loop or in a previous loop.
                var currentValue = coord.ID + 1; // +1 because 0 is unvisited.
                var super = coord;
                var depth = 0;
                for (; depth < coords.Length; depth++)
                {
                    var visitedValue = visitedCoords[super.ID];
                    if (visitedValue == currentValue)
                    {
                        return true; // Circular reference. We've already visited this coord this loop.
                    }
                    else if (visitedValue > 0)
                    {
                        break; // Safe. Coord that was already checked during a previous loop.
                    }
                    // Coord hasn't been checked yet.
                    visitedCoords[super.ID] = currentValue;
                    super = super.Parent;
                    if (super == null)
                    {
                        break; // Safe. End of parents.
                    }
                }
                if (depth == coords.Length)
                {
                    return true; // Circular reference. More parents than number of coords means infinite recursion.
                }
            }
            return false; // Safe. No circular references found.
        }


        public static Coordinate[] CloneCoordiantes(Coordinate[] coords)
        {
            var newCoords = new Coordinate[coords.Length];
            for (var i = 0; i < coords.Length; i++)
            {
                newCoords[i] = new Coordinate(coords[i], newCoords);
            }
            return newCoords;
        }
    }
}
