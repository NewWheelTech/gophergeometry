﻿///////////////////////////////////////////////////////////////////////////////
// Gopher Geometry
// Copyright(C) 2023  Matthew Newberg

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.
///////////////////////////////////////////////////////////////////////////////

using g3;
using Rhino;
using Rhino.Commands;
using System.Collections.Generic;

namespace Gopher
{
    [System.Runtime.InteropServices.Guid("b4a8ebae-c194-48ab-986c-1475242009c8")]
    public class GopherCommand : Command
    {
        public GopherCommand()
        {
            // Rhino only creates one instance of each command class defined in a
            // plug-in, so it is safe to store a refence in a static property.
            Instance = this;
        }

        ///<summary>The only instance of this command.</summary>
        public static GopherCommand Instance
        {
            get; private set;
        }

        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName
        {
            get { return "GopherTestCommand"; }
        }

        public static DMesh3 MakeCappedCylinder(bool bNoSharedVertices, int nSlices = 16, bool bHole = false)
        {
            DMesh3 mesh = new DMesh3(true, false, false, true);
            CappedCylinderGenerator cylgen = new CappedCylinderGenerator()
            {
                NoSharedVertices = bNoSharedVertices,
                Slices = nSlices
            };
            cylgen.Generate();
            cylgen.MakeMesh(mesh);
            mesh.ReverseOrientation();
            if (bHole)
                mesh.RemoveTriangle(0);
            return mesh;
        }

        public static DMesh3 MakeRemeshedCappedCylinder(double fResFactor = 1.0)
        {
            DMesh3 mesh = MakeCappedCylinder(false, 128);
            MeshUtil.ScaleMesh(mesh, Frame3f.Identity, new g3.Vector3f(1, 2, 1));

            // construct mesh projection target
            DMesh3 meshCopy = new DMesh3(mesh);
            DMeshAABBTree3 tree = new DMeshAABBTree3(meshCopy);
            tree.Build();
            MeshProjectionTarget target = new MeshProjectionTarget()
            {
                Mesh = meshCopy,
                Spatial = tree
            };
            MeshConstraints cons = new MeshConstraints();
            EdgeRefineFlags useFlags = EdgeRefineFlags.NoFlip;
            foreach (int eid in mesh.EdgeIndices())
            {
                double fAngle = MeshUtil.OpeningAngleD(mesh, eid);
                if (fAngle > 30.0f)
                {
                    cons.SetOrUpdateEdgeConstraint(eid, new EdgeConstraint(useFlags));
                    Index2i ev = mesh.GetEdgeV(eid);
                    int nSetID0 = (mesh.GetVertex(ev[0]).y > 1) ? 1 : 2;
                    int nSetID1 = (mesh.GetVertex(ev[1]).y > 1) ? 1 : 2;
                    cons.SetOrUpdateVertexConstraint(ev[0], new VertexConstraint(true, nSetID0));
                    cons.SetOrUpdateVertexConstraint(ev[1], new VertexConstraint(true, nSetID1));
                }
            }
            Remesher r = new Remesher(mesh);
            r.SetExternalConstraints(cons);
            r.SetProjectionTarget(target);
            r.Precompute();
            r.EnableFlips = r.EnableSplits = r.EnableCollapses = true;
            r.MinEdgeLength = 0.1f * fResFactor;
            r.MaxEdgeLength = 0.2f * fResFactor;
            r.EnableSmoothing = true;
            r.SmoothSpeedT = 0.5f;
            for (int k = 0; k < 20; ++k)
                r.BasicRemeshPass();
            return mesh;
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            DMesh3 mesh = new DMesh3(MakeRemeshedCappedCylinder(1.0), true);
            AxisAlignedBox3d bounds = mesh.GetBounds();

            List<IMesh> result_meshes = new List<IMesh>();

            LaplacianMeshDeformer deformer = new LaplacianMeshDeformer(mesh);

            // constrain bottom points
            foreach (int vid in mesh.VertexIndices())
            {
                g3.Vector3d v = mesh.GetVertex(vid);
                bool bottom = (v.y - bounds.Min.y) < 0.01f;
                if (bottom)
                    deformer.SetConstraint(vid, v, 10);
            }

            // constrain one other vtx
            int ti = MeshQueries.FindNearestTriangle_LinearSearch(mesh, new g3.Vector3d(2, 5, 2));
            int v_pin = mesh.GetTriangle(ti).a;
            g3.Vector3d cons_pos = mesh.GetVertex(v_pin);
            cons_pos += new g3.Vector3d(0.5, 0.5, 0.5);
            deformer.SetConstraint(v_pin, cons_pos, 10);


            deformer.Initialize();
            g3.Vector3d[] resultV = new g3.Vector3d[mesh.MaxVertexID];
            deformer.Solve(resultV);

            foreach (int vid in mesh.VertexIndices())
                mesh.SetVertex(vid, resultV[vid]);
            

            var rhinoMesh = GopherUtil.ConvertToRhinoMesh(mesh);

            doc.Objects.AddMesh(rhinoMesh);

            return Result.Success;
        }
    }
}
