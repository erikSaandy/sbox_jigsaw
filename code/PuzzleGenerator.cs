using System.Collections;
using System.Collections.Generic;
using System.Net;
using System;
using Saandy;
using Sandbox;


partial class PuzzleGenerator : Entity
{
	public const string m_default = "materials/jigsaw_default.vmat";
	public const string m_backside = "materials/jigsaw_back.vmat";

	public const int backsideUVTiling = 4;
	public const float pieceThickness = 0.06f;
	public const int scale = 32;
	private const int PipPointCount = 12;
	private const int BodyPointCount = 12;

	[Net] private static bool IsGenerated { get; set; } = false;

	[Net] public static PuzzleGenerator Instance { get; set; }
	[Net] public static Material DefaultImageMat { get; set; }
	[Net] public static Material BacksideMat { get; set; }

	[Net] public static Texture PuzzleImage { get; set; }

	public int width, height;
	public int maxDimension;

	public float wobbleAmount = 0.15f;
	[Net] public PieceData[,] pieces { get; set; }
	public int divisionLevel = 1;

	private static MeshBuilder mesher = null;
    private static int pieceCount;


    [Net, Predicted] public PuzzleManager CurrentManager { get; set; }

	[ClientRpc]
	public static void LoadMaterialsOnClient()
	{
		DefaultImageMat = Material.Load( m_default );
		BacksideMat = Material.Load( m_backside );
		PuzzleImage = Texture.Load( "textures/kittens.png" );
	}

	public void LoadMaterialAttributesOnServer()
	{
		LoadMaterialsOnClient();
		Log.Warning( "Load materials" );
		DefaultImageMat = Material.Load( m_default );
		BacksideMat = Material.Load( m_backside );
		PuzzleImage = Texture.Load( "textures/kittens.png" );
	}

    public PuzzleGenerator() {
		if ( Instance == null ) {
			LoadMaterialAttributesOnServer();
			Instance = this;
		}
		else if ( Instance != this ) {
			Delete();
		}
    }

    public void GeneratePuzzle() {

		if ( IsGenerated ) { return; }


		float t = Time.Now;

        if (mesher == null)
            mesher = new MeshBuilder();

		GetDimensions( out pieceCount );
		pieces = new PieceData[width, height];

		//DefaultImageMat.OverrideTexture( "Color", PuzzleImage );
		CurrentManager = new PuzzleManager( width, height, m_default, "" );

		//Iterate through the array "randomly".
		int p = 37;
		int s = width * height;
		int q = p % s;
		for ( int i = 0; i < s; i++ )
		{
			GeneratePiece( q % width, q / width );
			q = (q + p) % s;
		}

		IsGenerated = true;

		//GeneratePiece( Rand.Int( 0, width - 1 ), Rand.Int( 0, height - 1 ) );

		//GeneratePiece( 0, 0 );
	}


	private void GetDimensions( out int pieceCount )
	{

		int pictureWidth = PuzzleImage == null? 10 : PuzzleImage.Width;
		int pictureHeight = PuzzleImage == null ? 10 : PuzzleImage.Height;

		int gcf = Math2d.GetGreatestCommonFactor( pictureWidth, pictureHeight );

		// Pixels on height and width.
		int whPixels = gcf / divisionLevel;

		width = pictureWidth / whPixels;
		height = pictureHeight / whPixels;

		while ( width * height < 100 * divisionLevel )
		{
			width *= 2;
			height *= 2;
		}

		maxDimension = width > height ? width : height;
		pieceCount = width * height;

	}

	public void GeneratePiece( int x, int y ) {

		int wobbleLeft = x == 0 ? 0 : 1;
		int wobbleRight = x == width - 1 ? 0 : 1;
		int wobbleBottom = y == 0 ? 0 : 1;
		int wobbleTop = y == height - 1 ? 0 : 1;

		PieceData pieceData = new PieceData(
			x, y,

			new Vector2(
				(x * scale) + (GetWobbleAt( x, y ) * wobbleLeft),
				(y * scale) + (GetWobbleAt( x, y ) * wobbleBottom)
			),
			new Vector2(
				(x * scale) + (GetWobbleAt( x, y + 1 ) * wobbleLeft),
				(y * scale) + scale + (GetWobbleAt( x, y + 1 ) * wobbleTop)
			),
			new Vector2(
				(x * scale) + scale + (GetWobbleAt( x + 1, y + 1 ) * wobbleRight),
				(y * scale) + scale + (GetWobbleAt( x + 1, y + 1 ) * wobbleTop)
			),
			new Vector2(
				(x * scale) + scale + (GetWobbleAt( x + 1, y ) * wobbleRight),
				(y * scale) + (GetWobbleAt( x + 1, y ) * wobbleBottom)
			),

			wobbleLeft == 0,
			wobbleRight == 0,
			wobbleTop == 0,
			wobbleBottom == 0
		);


		GetBodyPoints( ref pieceData );

		EarClipping.Process( pieceData.polygon.contour, out pieceData.tris );

		Vector3 pos = new Vector3( x, y, 0 ) * scale;

		// Mesh building //

		mesher.GenerateMesh( pieceData, pos, out Mesh[] m );

		PuzzlePiece pieceEntity = new PuzzlePiece();
		pieceEntity.Spawn( x, y, m, CurrentManager );

		pieceEntity.Position = pos += Vector3.Up * 512;

		//pieceEntity.Rotation = Rotation.RotateAroundAxis( Vector3.Up, 180 );

		Log.Warning( "CURRENT MANAGER IS VALID : " );
		Log.Warning( CurrentManager != null );
        CurrentManager.AddPiece(pieceEntity, x, y);
        pieces[x, y] = pieceData;

    }

    private void GetBodyPoints(ref PieceData piece) {

        int sidePointCount = BodyPointCount / 4;

        for (int i = 0; i < 4; i++) {
            piece.contourSideStartID[i] = piece.polygon.contour.Count;

            Math2d.Line side = piece.GetSide(i);
            Vector2 sideDir = piece.GetSideNormal(i);
            bool needsPip = !piece.SideIsOnEdge(i); //  needs pip if side isn't on edge.

			// Has neighbor on side
			if (needsPip && pieces[piece.x + (int)sideDir.x, piece.y + (int)sideDir.y] != null) {

				PieceData neighbor = pieces[piece.x + (int)sideDir.x, piece.y + (int)sideDir.y];
                int neighborSideID = Math2d.ClampListIndex(i + 2, 4);
                int sideStartId = pieces[(int)neighbor.x, (int)neighbor.y].contourSideStartID[neighborSideID] + 1;

                neighborSideID = Math2d.ClampListIndex(i + 3, 4);
                int sideEndId = Math2d.ClampListIndex(pieces[(int)neighbor.x, (int)neighbor.y].contourSideStartID[neighborSideID] - 1, neighbor.polygon.contour.Count);            

                int sideLength = sideEndId - sideStartId + 1;

                List<Vector2> sidePoints = neighbor.polygon.contour.points.GetRange(sideStartId, sideLength);
                sidePoints.Reverse();

                piece.polygon.contour.points.Add(side.pointA);
                piece.polygon.contour.points.AddRange(sidePoints);

            }
            else {

                float step = side.Magnitude / sidePointCount;

                float j = 0;
                float pipStart = 0.4f * scale;
                float pipEnd = 0.6f * scale;

                do {

                    if ( needsPip && j + step >= pipStart ) {
                        AddPipPoints(ref piece, i, pipStart, pipEnd);

                        j = pipEnd + 0.1f;
                        needsPip = false;
                    }

                    bool wobbleX = (!piece.SideIsOnEdge(0) && i == 0) && (!piece.SideIsOnEdge(2) && i == 2);
                    bool wobbleY = (!piece.SideIsOnEdge(1) && i == 1) && (!piece.SideIsOnEdge(3) && i == 3);

                    Vector2 p = GetWobblePositionAt(side.pointA + (side.Direction * j), wobbleX, wobbleY);
                    //Math2d.DrawPoint(p, Color.white, 5);

                    piece.polygon.contour.Add(p);
                    j += step;

                } while (j < side.Magnitude);

            }

        }
    }

    private void AddPipPoints(ref PieceData piece, int sideIndex, float start, float end) {
        Vector2 a, b, c, d;

        Vector2 up = Vector2.Zero;
        switch (sideIndex) {
            case 0:
                up = Vector2.Right;
                break;
            case 1:
                up = Vector2.Up;
                break;
            case 2:
                up = Vector2.Left;
                break;
            case 3:
                up = Vector2.Down;
                break;
        }

        Vector2 left = Math2d.RotateByAngle(up, -90f) * scale;
        Vector2 right = Math2d.RotateByAngle(up, 90f ) * scale;
		up *= scale;

        Math2d.Line side = piece.GetSide(sideIndex);

        a = side.pointA + (side.Direction * start);
        b = a + (up * 0.35f) + (left * 0.35f);
        d = side.pointA + (side.Direction * end);
        c = d + (up * 0.35f) + (right * 0.35f);
        int pointCount = PipPointCount;
        for (int i = 0; i <= pointCount; i++) {
            float t = ((i) / (float)pointCount);
            Vector2 p = Math2d.CubicCurve(a, b, c, d, t);
            piece.polygon.contour.Add(p);
        } 

    }

    public static Vector2 GetWobblePositionAt(Vector2 position, bool wobbleX = true, bool wobbleY = true) {
        return GetWobblePositionAt(position.x, position.y, wobbleX, wobbleY);
    }

    public static Vector2 GetWobblePositionAt(float x, float y, bool wobbleX = true, bool wobbleY = true) {
        return new Vector2(x + (GetWobbleAt(x, y) * (wobbleX ? 1 : 0)), y + GetWobbleAt(x, y) * (wobbleY ? 1 : 0));
    }


    public static Vector2 GetWobblePositionAt(Vector2 pos) {
        return new Vector2(pos.x + GetWobbleAt(pos.x, pos.y), pos.y + GetWobbleAt(pos.x, pos.y));
    }

    public static float GetWobbleAt(float x, float y) {
		return (Noise.Perlin( x * 0.1f, y * 0.1f, 0 )) * Instance.wobbleAmount * 64;
    }

}

public partial class PieceData {

    public int x, y;

    public Triangulation.Polygon polygon;

    public List<Triangle> tris;

    public Vector2[] corners;
    private readonly Math2d.Line[] straightSides;
    private readonly bool[] isEdge;

    // What ID is the first point of the side?
    public int[] contourSideStartID;

    public PieceData(int x, int y, Vector2 bl, Vector2 tl, Vector2 tr, Vector2 br, bool edgeLeft, bool edgeRight, bool edgeTop, bool edgeBottom) {
        this.x = x;
        this.y = y;
		
        polygon = new Triangulation.Polygon();

        corners = new Vector2[4] { bl, tl, tr, br };
		straightSides = new Math2d.Line[4] { new Math2d.Line(bl, tl), new Math2d.Line(tl, tr), new Math2d.Line(tr, br), new Math2d.Line(br, bl) };
		isEdge = new bool[4] { edgeLeft, edgeTop, edgeRight, edgeBottom };
        center.Set((corners[0].x + corners[3].x) / 2, (corners[1].y + corners[0].y) / 2);

        contourSideStartID = new int[4];
    }

    private Vector2 center = Vector2.Zero;
    public Vector2 Center => center;
  

    public Vector2 GetSideNormal(int index) {
        switch (index) {
            case 0:
                return new Vector2(-1, 0); // left
            case 1:
                return new Vector2(0, 1);  // top
            case 2:
                return new Vector2(1, 0);  // right
            case 3:
                return new Vector2(0, -1);  // bottom
            default:
                return Vector2.Zero;
        }
    }

    // left, top, right, bottom
    public Math2d.Line GetSide(int index) { return straightSides[index]; }
    public bool SideIsOnEdge(int side) { return isEdge[side]; }

}

public class MeshBuilder
{
	public static VertexBuffer vb;
	private int vertexCount;
	public int mesh1VertexCount { get; private set; }

	public MeshBuilder() {
		vb = new VertexBuffer();
		vb.Init( true );
	}

	public void GenerateMesh( PieceData piece, Vector2 position, out Mesh[] m )
	{
		vb.Clear();
		vertexCount = 0;

		int triCount = piece.tris.Count;

		// Front face.
		for ( int i = 0; i < triCount; i++ )
		{
			AddTri( piece.tris[i], position );
		}

		mesh1VertexCount = vertexCount;

		// Back face.
		for ( int i = 0; i < triCount; i++ )
		{
			AddTri( piece.tris[i], position, true );
		}

		AddTrim( position, piece );


		Material mat = Material.Load( PuzzleGenerator.m_default );
		Mesh m1 = new Mesh( mat );
		m1.Material = mat;
		m1.SetBounds( -PuzzleGenerator.scale / 2, PuzzleGenerator.scale / 2 );
		m1.CreateBuffers( vb, true );
		m1.SetIndexRange( 0, mesh1VertexCount );


		mat = Material.Load( PuzzleGenerator.m_backside );
		Mesh m2 = new Mesh( mat );
		m2.Material = mat;
		m2.SetBounds( -PuzzleGenerator.scale / 2, PuzzleGenerator.scale / 2 );
		m2.CreateBuffers( vb, true );
		m2.SetIndexRange( mesh1VertexCount, vertexCount );

		m = new Mesh[2] { m1, m2 };

	}

	private void AddTri(Saandy.Triangle tri, Vector3 position, bool backside = false)
	{
		AddTri( tri.v1, tri.v2, tri.v3, position, backside );
	}

	private void AddTri( Vector3 v1, Vector3 v2, Vector3 v3, Vector3 position, bool backside = false )
	{
		//position += new Vector3( PuzzleGenerator.scale, PuzzleGenerator.scale, 0 ) / 2;
		position += new Vector3( PuzzleGenerator.scale, PuzzleGenerator.scale, 0 ) / 2;
		Vector3 thicknessOffset = Vector3.Up * PuzzleGenerator.scale * (PuzzleGenerator.pieceThickness / 2);

		float uMax = PuzzleGenerator.scale * PuzzleGenerator.Instance.width;
		float vMax = PuzzleGenerator.scale * PuzzleGenerator.Instance.height;
		if (backside)
		{
			vb.AddTriangle(
				new Sandbox.Vertex(
					v3 - position - thicknessOffset,
					Vector3.Down,
					Vector3.Backward,
					Vector2.One - new Vector2( v3.x / (uMax / PuzzleGenerator.backsideUVTiling), v3.y / (uMax / PuzzleGenerator.backsideUVTiling) ) ),

				new Sandbox.Vertex(
					v2 - position - thicknessOffset,
					Vector3.Down,
					Vector3.Backward,
					Vector2.One - new Vector2( v2.x / (uMax / PuzzleGenerator.backsideUVTiling), v2.y / (uMax / PuzzleGenerator.backsideUVTiling) ) ),

				new Sandbox.Vertex(
					v1 - position - thicknessOffset,
					Vector3.Down,
					Vector3.Backward,
					Vector2.One - new Vector2( v1.x / (uMax / PuzzleGenerator.backsideUVTiling), v1.y / (uMax / PuzzleGenerator.backsideUVTiling) ) )
			);
		}	
		else
		{
			vb.AddTriangle(
				new Sandbox.Vertex(
					v1 - position + thicknessOffset,
					Vector3.Up,
					Vector3.Forward,
					new Vector2( v1.x / uMax, 1 - (v1.y / vMax) ) ),

				new Sandbox.Vertex(
					v2 - position + thicknessOffset,
					Vector3.Up,
					Vector3.Forward,
					new Vector2( v2.x / uMax, 1 - (v2.y / vMax) ) ),

				new Sandbox.Vertex(
					v3 - position + thicknessOffset,
					Vector3.Up,
					Vector3.Forward,
					new Vector2( v3.x / uMax, 1 - (v3.y / vMax) ) )
			);
		}

		vertexCount += 3;

	}

	private void AddTrim( Vector2 position, PieceData piece )
	{
		position += new Vector2( PuzzleGenerator.scale ) / 2;
		Vector3 thicknessOffset = Vector3.Up * PuzzleGenerator.scale * (PuzzleGenerator.pieceThickness / 2);

		// Initial points on trim //
		Vector3 a = ( Vector3)piece.polygon.contour.points[0] - (Vector3)position - thicknessOffset;
		Vector3 b = ( Vector3)piece.polygon.contour.points[0] - (Vector3)position + thicknessOffset;
		Vector3 c = ( Vector3)piece.polygon.contour.points[1] - (Vector3)position + thicknessOffset;
		Vector3 d = ( Vector3)piece.polygon.contour.points[1] - (Vector3)position - thicknessOffset;

		Vector3 tangent = (d - a);
		Vector3 nrmlB = Vector3.Cross( tangent, (a - b) );

		int contourCount = piece.polygon.contour.Count;
		float uvStepX = 0.1f * (d - a).Length / PuzzleGenerator.backsideUVTiling;
		float uvStepY = PuzzleGenerator.pieceThickness / 2f;

		vb.AddQuad(
			new Sandbox.Vertex( a, nrmlB, tangent, new Vector2( 0, 0 ) ),
			new Sandbox.Vertex( b, nrmlB, tangent, new Vector2( 0, uvStepY ) ),
			new Sandbox.Vertex( c, nrmlB, tangent, new Vector2( uvStepX, uvStepY )),
			new Sandbox.Vertex( d, nrmlB, tangent, new Vector2( uvStepX, 0 ) ) 
		);

		vertexCount += 4;

		float cStep = 0;
		float angle = 0;
		for ( int i = 0; i < contourCount; i++ )
		{

			nrmlB = Vector3.Cross( tangent, (a - b).Normal );

			a = d;
			b = c;
			c = ( Vector3 )piece.polygon.contour.points[Math2d.ClampListIndex( i + 1, contourCount )] - (Vector3)position + thicknessOffset;
			d = ( Vector3 )piece.polygon.contour.points[Math2d.ClampListIndex( i + 1, contourCount )] - (Vector3)position - thicknessOffset;

			tangent = (d - a);
			Vector3 nrmlA = Vector3.Cross( tangent, (a - b) );

			cStep += uvStepX;
			uvStepX = 0.1f * (d - a).Length / PuzzleGenerator.backsideUVTiling;

			vertexCount += 4;

			vb.AddQuad(
				new Sandbox.Vertex( a, nrmlA, tangent, new Vector2( cStep, 0 ) ),
				new Sandbox.Vertex( b, nrmlA, tangent, new Vector2( cStep, uvStepY ) ),
				new Sandbox.Vertex( c, nrmlA, tangent, new Vector2( cStep + uvStepX, uvStepY ) ),
				new Sandbox.Vertex( d, nrmlA, tangent, new Vector2( cStep + uvStepX, 0 ) )
			);

			angle = Math.Abs( Math2d.Angle3D( nrmlB, nrmlA, a-b ) );

		}
	}

}

