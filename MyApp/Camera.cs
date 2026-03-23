using SharpDX;

namespace ProiectSPG
{
    /// <summary>
    /// Created using the model presented by Frank Luna in "Introduction to 3D Game Programming" book  
    /// The data of the camera class stores two key pieces of information. The position, right, up, and look vectors of the camera defining, respectively, 
    /// the origin, x-axis, y-axis, and z-axis of the view space coordinate system in world coordinates, and the properties of the frustum.
    /// </summary>
    public class Camera
    {
        private bool viewDirty = true;

        public Camera()
        {
            SetLens(MathUtil.PiOverFour, 1.0f, 1.0f, 1000.0f);
        }

        public Vector3 Position { get; set; }
        public Vector3 Right { get; private set; } = Vector3.UnitX;
        public Vector3 Up { get; private set; } = Vector3.UnitY;
        public Vector3 Look { get; private set; } = Vector3.UnitZ;

        public float NearZ { get; private set; }
        public float FarZ { get; private set; }
        public float Aspect { get; private set; }
        public float FovY { get; private set; }

        public float NearWindowHeight { get; private set; }
        public float NearWindowWidth => Aspect * NearWindowHeight;
        public float FarWindowHeight { get; private set; }

        public Matrix View { get; private set; } = Matrix.Identity;
        public Matrix Proj { get; private set; } = Matrix.Identity;


        /// <summary>
        /// This method controls our view. We cache the frustum properties and build the projection matrix.
        /// </summary>
        /// <param name="fovY"></param>
        /// <param name="aspect"></param>
        /// <param name="zn"></param>
        /// <param name="zf"></param>
        public void SetLens(float fovY, float aspect, float zn, float zf)
        {
            FovY = fovY;
            Aspect = aspect;
            NearZ = zn;
            FarZ = zf;

            NearWindowHeight = 2.0f * zn * MathHelper.Tanf(0.5f * fovY);
            FarWindowHeight = 2.0f * zf * MathHelper.Tanf(0.5f * fovY);

            Proj = Matrix.PerspectiveFovLH(fovY, aspect, zn, zf);
        }

        /// <summary>
        /// Move the camera along its right vector to strafe right and left.
        /// </summary>
        /// <param name="d"></param>
        public void Strafe(float d)
        {
            Position += Right * d;
            viewDirty = true;
        }

        /// <summary>
        /// Move the camera along its look vector to move forwards and backwards.
        /// </summary>
        /// <param name="d"></param>
        public void Walk(float d)
        {
            Position += Look * d;
            viewDirty = true;
        }

        /// <summary>
        /// Rotate the camera around its right vector to look up and down.
        /// </summary>
        /// <param name="angle"></param>
        public void Pitch(float angle)
        {
            // Rotate up and look vector about the right vector.
            Matrix r = Matrix.RotationAxis(Right, angle);

            Up = Vector3.TransformNormal(Up, r);
            Look = Vector3.TransformNormal(Look, r);

            viewDirty = true;
        }

        /// <summary>
        /// Rotate the camera around the world’s y-axis (assuming the y-axis corresponds to the world’s “up” direction) 
        /// vector to look right and left.
        /// </summary>
        /// <param name="angle"></param>
        public void RotateY(float angle)
        {
            // Rotate the basis vectors about the world y-axis.
            Matrix r = Matrix.RotationY(angle);

            Right = Vector3.TransformNormal(Right, r);
            Up = Vector3.TransformNormal(Up, r);
            Look = Vector3.TransformNormal(Look, r);

            viewDirty = true;
        }

        public void UpdateViewMatrix()
        {
            if (!viewDirty) return;

            // Keep camera's axes orthogonal to each other and of unit length.
            Look = Vector3.Normalize(Look);
            Up = Vector3.Normalize(Vector3.Cross(Look, Right));

            // U, L already ortho-normal, so no need to normalize cross product.
            Right = Vector3.Cross(Up, Look);

            // Fill in the view matrix entries.
            float x = -Vector3.Dot(Position, Right);
            float y = -Vector3.Dot(Position, Up);
            float z = -Vector3.Dot(Position, Look);

            View = new Matrix(
                Right.X, Up.X, Look.X, 0.0f,
                Right.Y, Up.Y, Look.Y, 0.0f,
                Right.Z, Up.Z, Look.Z, 0.0f,
                x, y, z, 1.0f
            );

            viewDirty = false;
        }
        public void LookAt(Vector3 pos, Vector3 target, Vector3 up)
        {
            Position = pos;

            Vector3 zaxis = Vector3.Normalize(target - pos); // forward
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, zaxis)); // right
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis); // up

            Right = xaxis;
            Up = yaxis;
            Look = zaxis;

            UpdateViewMatrix();
        }
    }



    

   
    }