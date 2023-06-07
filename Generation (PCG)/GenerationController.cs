using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using UnityEditor;
using Random = UnityEngine.Random;

/*
* Carl Eriksson
* 2023-06-02
*/

[CustomEditor(typeof(GenerationController))]
public class GenerationControllerEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();
        GenerationController gc = (GenerationController)target;

        if (GUILayout.Button("GENERATE")) {
            gc.Generate();
        }
        if (GUILayout.Button("CLEAR")) {
            gc.Clear();
        }
        EditorUtility.SetDirty(gc);
    }
}

[System.Serializable]
public class Tile {
    public enum TileType { NONE, CORRIDOR, ROOM, DOORWAY };

    public int taken;
    public GameObject obj;
    public TileType tileType;
    public bool filled = false;

    public Tile(int taken, GameObject obj, TileType tileType, bool filled) {
        this.taken = taken;
        this.obj = obj;
        this.tileType = tileType;
        this.filled = filled;
    }
}

public class GenerationController : MonoBehaviour {
    [Header("ASSIGNABLES: ")]
    [SerializeField] private GameObject tile = null;
    [SerializeField] private Material mat = null;
    [SerializeField] private GameObject[] prefabs = null;
    [SerializeField] private LayerMask lm = 0;

    [Header("MESH: ")]
    [SerializeField] private int mapSize = 16;
    [SerializeField] private int roomGoal = 8;
    [SerializeField] private int smoothness = 2;
    [SerializeField] private int maxRoomRadius = 2;
    [SerializeField] private float meshScaling = 3;
    [SerializeField] private float dungeonHeight = 1;

    [Header("SPAWNING: ")]
    [SerializeField] private int enemySpawnCount = 5;
    [SerializeField] private GameObject[] enemyPrefabs = null;

    [Header("DEBUG: ")]
    [SerializeField] private bool generateOnStart = false;
    [SerializeField] private bool drawGizmos = false;
    [SerializeField] private bool spawnEnemies = false;
    [SerializeField] private bool spawnPrefabs = false;

    private Tile[,] grid = null;
    private int retries = 0;
    private GameObject prefabWrapper = null;
    private Transform player = null;
    private readonly List<GameObject> spawnedPrefabs = new List<GameObject>();
    private readonly List<Vector3> possibleSpawnPoints = new List<Vector3>();

    private void OnDrawGizmos() {
        if (drawGizmos) {
            Gizmos.DrawCube(transform.position + new Vector3(Mathf.RoundToInt(mapSize / 2) - 0.5f,
                -1, Mathf.RoundToInt(mapSize / 2) - 0.5f) * meshScaling, new Vector3(mapSize, 0, mapSize)  * meshScaling);

            if (grid != null && grid.GetLength(0) > 0) {
                for (int x = 0; x < grid.GetLength(0); x++) {
                    for (int y = 0; y < grid.GetLength(1); y++) {
                        if (grid[x, y].tileType == Tile.TileType.NONE) continue;
                        if (grid[x, y].tileType == Tile.TileType.ROOM) Gizmos.color = Color.red;
                        if (grid[x, y].tileType == Tile.TileType.CORRIDOR) Gizmos.color = Color.blue;
                        if (grid[x, y].tileType == Tile.TileType.DOORWAY) Gizmos.color = Color.green;
                        Gizmos.DrawCube(new Vector3(x * meshScaling, 1, y * meshScaling), Vector3.one * meshScaling);
                    }
                }
            }
        }
    }

    private void Start() {
        player = FindObjectOfType<MovementController>().transform;
        if(generateOnStart) Generate();
    }

    public void Generate() {
        ScaleMap(1f);
        Clear();
        CreateGrid();

        //limit room goal if higher than mapsize
        if (roomGoal > grid.GetLength(0) * grid.GetLength(1)) {
            roomGoal = (grid.GetLength(0) * grid.GetLength(1));
        }

        GenerateRooms();
        GenerateCorridors();
        GenerateDoorways();
        Cleanup();
        FloodFill();
        GenerateFloorMesh();
        GenerateSpawnPoints();
        if (spawnPrefabs) SpawnPrefabs();
        GenerateWallMesh();
        MergeMesh();
        RotatePrefabs();

        if(spawnPrefabs && prefabWrapper != null) prefabWrapper.transform.SetParent(transform, false);
        ScaleMap(meshScaling);
        PlacePlayer();
        if (spawnEnemies) SpawnEnemies();
    }

    public void Clear() {
        possibleSpawnPoints.Clear();
        spawnedPrefabs.Clear();
        highestSection = 0;
        if (transform.childCount > 0) {
            for (int i = transform.childCount - 1; i >= 0; i--) {
                Transform child = transform.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
        }
        ClearLog();
    }

    private static void ClearLog() {
        Assembly assembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));
        Type type = assembly.GetType("UnityEditor.LogEntries");
        MethodInfo method = type.GetMethod("Clear");
        if (method != null) method.Invoke(new object(), null);
    }

    private void CreateGrid() {
        grid = new Tile[mapSize, mapSize];

        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                grid[x, y] = new Tile(0, null, Tile.TileType.NONE, false);
            }
        }
    }
    
    private void MergeMesh() {
        List<MeshRenderer> allMeshes = new List<MeshRenderer>();
        if (transform.childCount > 0) {
            for (int i = transform.childCount - 1; i >= 0; i--) {
                allMeshes.Add(transform.GetChild(i).GetComponent<MeshRenderer>());
            }
        }

        Mesh finalMesh = new Mesh();
        CombineInstance[] combineInstance = new CombineInstance[allMeshes.Count];
        for (int i = 0; i < allMeshes.Count; i++) {
            combineInstance[i].mesh = allMeshes[i].GetComponent<MeshFilter>().sharedMesh;
            combineInstance[i].transform = allMeshes[i].transform.localToWorldMatrix;
        }

        finalMesh.CombineMeshes(combineInstance);
        GameObject newObject = new GameObject("mesh");
        MeshFilter filter = newObject.AddComponent<MeshFilter>();
        filter.sharedMesh = finalMesh;
        MeshRenderer ren = newObject.AddComponent<MeshRenderer>();
        ren.sharedMaterial = mat;

        for (int i = transform.childCount - 1; i >= 0; i--) {
            Transform child = transform.GetChild(i);
            DestroyImmediate(child.gameObject);
        }

        newObject.transform.SetParent(transform, false);
    }

    private void GenerateSpawnPoints() {
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (grid[x, y].taken == 1 && grid[x, y].tileType == Tile.TileType.ROOM) {
                    possibleSpawnPoints.Add(grid[x, y].obj.transform.position);
                }
            }
        }
    }

    private void GenerateWallMesh() {
        Mesh mesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector3> normals = new List<Vector3>();

        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (grid[x, y].taken == 1) {
                    // top neighbor
                    if (y < grid.GetLength(1) - 1 && grid[x, y + 1].taken == 0) {
                        Vector3 topLeft = new Vector3(x, dungeonHeight, y + 1);
                        Vector3 topRight = new Vector3(x + 1, dungeonHeight, y + 1);
                        Vector3 bottomLeft = new Vector3(x, 0, y + 1);
                        Vector3 bottomRight = new Vector3(x + 1, 0, y + 1);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }

                    // right neighbor
                    if (x < grid.GetLength(0) - 1 && grid[x + 1, y].taken == 0) {
                        Vector3 topLeft = new Vector3(x + 1, dungeonHeight, y + 1);
                        Vector3 topRight = new Vector3(x + 1, dungeonHeight, y);
                        Vector3 bottomLeft = new Vector3(x + 1, 0, y + 1);
                        Vector3 bottomRight = new Vector3(x + 1, 0, y);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }

                    // bottom neighbor
                    if (y > 0 && grid[x, y - 1].taken == 0) {
                        Vector3 topLeft = new Vector3(x + 1, dungeonHeight, y);
                        Vector3 topRight = new Vector3(x, dungeonHeight, y);
                        Vector3 bottomLeft = new Vector3(x + 1, 0, y);
                        Vector3 bottomRight = new Vector3(x, 0, y);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }

                    // left neighbor
                    if (x > 0 && grid[x - 1, y].taken == 0) {
                        Vector3 topLeft = new Vector3(x, dungeonHeight, y);
                        Vector3 topRight = new Vector3(x, dungeonHeight, y + 1);
                        Vector3 bottomLeft = new Vector3(x, 0, y);
                        Vector3 bottomRight = new Vector3(x, 0, y + 1);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }

                    //check end of grid too
                    if (y + 1 >= grid.GetLength(1)) {
                        Vector3 topLeft = new Vector3(x, dungeonHeight, y + 1);
                        Vector3 topRight = new Vector3(x + 1, dungeonHeight, y + 1);
                        Vector3 bottomLeft = new Vector3(x, 0, y + 1);
                        Vector3 bottomRight = new Vector3(x + 1, 0, y + 1);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }
                    if (x + 1 >= grid.GetLength(0)) {
                        Vector3 topLeft = new Vector3(x + 1, dungeonHeight, y + 1);
                        Vector3 topRight = new Vector3(x + 1, dungeonHeight, y);
                        Vector3 bottomLeft = new Vector3(x + 1, 0, y + 1);
                        Vector3 bottomRight = new Vector3(x + 1, 0, y);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }
                    if (y - 1 < 0) {
                        Vector3 topLeft = new Vector3(x + 1, dungeonHeight, y);
                        Vector3 topRight = new Vector3(x, dungeonHeight, y);
                        Vector3 bottomLeft = new Vector3(x + 1, 0, y);
                        Vector3 bottomRight = new Vector3(x, 0, y);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }
                    if (x - 1 < 0) {
                        Vector3 topLeft = new Vector3(x, dungeonHeight, y);
                        Vector3 topRight = new Vector3(x, dungeonHeight, y + 1);
                        Vector3 bottomLeft = new Vector3(x, 0, y);
                        Vector3 bottomRight = new Vector3(x, 0, y + 1);
                        int startIndex = vertices.Count;
                        vertices.Add(topLeft);
                        vertices.Add(topRight);
                        vertices.Add(bottomLeft);
                        vertices.Add(bottomRight);
                        triangles.Add(startIndex);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 2);
                        triangles.Add(startIndex + 1);
                        triangles.Add(startIndex + 3);
                        triangles.Add(startIndex + 2);
                    }
                }
            }
        }

        Vector2[] uvs = new Vector2[vertices.Count];

        for (int i = 0; i < triangles.Count; i += 3) {
            Vector3 v1 = vertices[triangles[i]];
            Vector3 v2 = vertices[triangles[i + 1]];
            Vector3 v3 = vertices[triangles[i + 2]];

            Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
            normals.Add(normal);

            if (normal == Vector3.forward || normal == -Vector3.forward) {
                uvs[triangles[i]] = new Vector2(v1.x, v1.y);
                uvs[triangles[i + 1]] = new Vector2(v2.x, v2.y);
                uvs[triangles[i + 2]] = new Vector2(v3.x, v3.y);
            } else if (normal == Vector3.right || normal == -Vector3.right) {
                uvs[triangles[i]] = new Vector2(v1.z, v1.y);
                uvs[triangles[i + 1]] = new Vector2(v2.z, v2.y);
                uvs[triangles[i + 2]] = new Vector2(v3.z, v3.y);
            } else if (normal == Vector3.up || normal == -Vector3.up) {
                uvs[triangles[i]] = new Vector2(v1.x, v1.z);
                uvs[triangles[i + 1]] = new Vector2(v2.x, v2.z);
                uvs[triangles[i + 2]] = new Vector2(v3.x, v3.z);
            }
        }

        // Make sure that normals is the same length as vertices
        if(normals.Count < vertices.Count) {
            for (int i = normals.Count; i < vertices.Count; i++) {
                normals.Add(Vector3.zero);
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        GameObject newObject = new GameObject("WallMesh");
        MeshFilter meshFilter = newObject.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;
        MeshRenderer meshRenderer = newObject.AddComponent<MeshRenderer>();
        meshRenderer.material = mat;
        newObject.transform.SetParent(transform, false);
        newObject.transform.position += new Vector3(-0.5f, 0.0f, -0.5f);
    }

    private void GenerateFloorMesh() {
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (grid[x, y].taken != 1) continue;
                GameObject newTile = Instantiate(tile, transform, false) as GameObject;
                newTile.transform.position = new Vector3(x, 0, y);
                newTile.transform.eulerAngles = new Vector3(90, 0, 0);
                grid[x, y].obj = newTile;
            }
        }
    }

    private void GenerateRooms() {
        for (int i = 0; i < roomGoal; i++) {
            GenerateRoom();
        }
    }

    private void GenerateRoom() {
        int roomSize = Random.Range(1, maxRoomRadius + 1);
        Vector2Int pos = new Vector2Int(Random.Range(roomSize, mapSize - roomSize), Random.Range(roomSize, mapSize - roomSize));
        bool canPlace = true;

        for (int x = pos.x - roomSize; x <= pos.x + roomSize; x++) {
            for (int y = pos.y - roomSize; y <= pos.y + roomSize; y++) {
                if (!CanPlaceTile(new Vector2Int(x, y))) {
                    canPlace = false;
                    break;
                }
            }
            if (!canPlace) {
                break;
            }
        }
        if (canPlace) {
            for (int x = pos.x - roomSize; x <= pos.x + roomSize; x++) {
                for (int y = pos.y - roomSize; y <= pos.y + roomSize; y++) {
                    grid[x, y].taken = 1;
                    grid[x, y].tileType = Tile.TileType.ROOM;
                }
            }
        }
        retries++;
        if (retries < 10) {
            GenerateRoom();
            return;
        }
        retries = 0;
    }

    private bool CanPlaceTile(Vector2Int position) {
        return grid[position.x, position.y].taken == 0;
    }

    private int NeighbourCount(Vector2Int pos) {
        int amount = 0;

        if(pos.x - 1 < 0 || pos.x + 1 >= grid.GetLength(0) ||
                pos.y - 1 < 0 || pos.y + 1 >= grid.GetLength(1)) {
            return 4;
        }

        if (grid[pos.x - 1, pos.y].taken == 1) {
            amount++;
        }
        if (grid[pos.x + 1, pos.y].taken == 1) {
            amount++;
        }
        if (grid[pos.x, pos.y - 1].taken == 1) {
            amount++;
        }
        if (grid[pos.x, pos.y + 1].taken == 1) {
            amount++;
        }

        return amount;
    }

    private static int NeighbourCountVisited(Vector2Int pos, bool[,] visited) {
        int amount = 0;

        if (pos.x - 1 < 0 || pos.x + 1 >= visited.GetLength(0) ||
                pos.y - 1 < 0 || pos.y + 1 >= visited.GetLength(1)) {
            return 4;
        }

        if (visited[pos.x - 1, pos.y] == true) {
            amount++;
        }
        if (visited[pos.x + 1, pos.y] == true) {
            amount++;
        }
        if (visited[pos.x, pos.y - 1] == true) {
            amount++;
        }
        if (visited[pos.x, pos.y + 1] == true) {
            amount++;
        }

        return amount;
    }

    private void GenerateCorridors() {
        for (int x = 1; x < mapSize - 1; x++) {
            for (int y = 1; y < mapSize - 1; y++) {
                if(grid[x, y].taken == 0 && NeighbourCount(new Vector2Int(x, y)) == 0) {
                    GenerateCorridor(new Vector2Int(x, y));
                }
            }
        }
    }

    private void GenerateCorridor(Vector2Int startPos) {
        bool[,] visited = new bool[mapSize, mapSize];
        int[,] path = new int[mapSize, mapSize];

        for (int x = 0; x < path.GetLength(0); x++) {
            for (int y = 0; y < path.GetLength(1); y++) {
                path[x, y] = 0;
            }
        }

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(startPos);

        while (stack.Count > 0) {
            Vector2Int pos = stack.Pop();
            if (visited[pos.x, pos.y]) {
                continue;
            }

            if(NeighbourCountVisited(new Vector2Int(pos.x, pos.y), visited) > 1) {
                continue;
            }

            visited[pos.x, pos.y] = true;
            path[pos.x, pos.y] = 1;
            if (pos.x == 0 || pos.x == mapSize - 1 || pos.y == 0 || pos.y == mapSize - 1) {
                break;
            }

            if (grid[pos.x - 1, pos.y].taken == 0 && NeighbourCount(new Vector2Int(pos.x - 1, pos.y)) == 0 && 
                    NeighbourCountVisited(new Vector2Int(pos.x - 1, pos.y), visited) <= 1) {
                stack.Push(new Vector2Int(pos.x - 1, pos.y));
            }
            if (grid[pos.x + 1, pos.y].taken == 0 && NeighbourCount(new Vector2Int(pos.x + 1, pos.y)) == 0 && 
                    NeighbourCountVisited(new Vector2Int(pos.x + 1, pos.y), visited) <= 1) {
                stack.Push(new Vector2Int(pos.x + 1, pos.y));
            }
            if (grid[pos.x, pos.y - 1].taken == 0 && NeighbourCount(new Vector2Int(pos.x, pos.y - 1)) == 0 && 
                    NeighbourCountVisited(new Vector2Int(pos.x, pos.y - 1), visited) <= 1) {
                stack.Push(new Vector2Int(pos.x, pos.y - 1));
            }
            if (grid[pos.x, pos.y + 1].taken == 0 && NeighbourCount(new Vector2Int(pos.x, pos.y + 1)) == 0 && 
                    NeighbourCountVisited(new Vector2Int(pos.x, pos.y + 1), visited) <= 1) {
                stack.Push(new Vector2Int(pos.x, pos.y + 1));
            }
        }

        if (path.GetLength(0) > 0) {
            for (int x = 0; x < path.GetLength(0); x++) {
                for (int y = 0; y < path.GetLength(1); y++) {
                    if (path[x, y] != 1) continue;
                    grid[x, y].taken = 1;
                    grid[x, y].tileType = Tile.TileType.CORRIDOR;
                }
            }
        }
    }

    private void GenerateDoorways() {
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (x - 1 < 0 || x + 1 >= grid.GetLength(0) ||
                        y - 1 < 0 || y + 1 >= grid.GetLength(1)) {
                    continue;
                }

                if (grid[x, y].taken != 0 ||
                    (((grid[x - 1, y].taken != 1 || grid[x - 1, y].tileType == Tile.TileType.DOORWAY) ||
                      (grid[x + 1, y].taken != 1 || grid[x + 1, y].tileType == Tile.TileType.DOORWAY)) &&
                     ((grid[x, y - 1].taken != 1 || grid[x, y - 1].tileType == Tile.TileType.DOORWAY) ||
                      (grid[x, y + 1].taken != 1 || grid[x, y + 1].tileType == Tile.TileType.DOORWAY))) ||
                    NeighbourCount(new Vector2Int(x, y)) != 2) continue;
                grid[x, y].taken = 1;
                grid[x, y].tileType = Tile.TileType.DOORWAY;
            }
        }
    }

    private void Cleanup() {
        //make a copy of grid
        Tile[,] newGrid = new Tile[grid.GetLength(0), grid.GetLength(1)];
        for (int i = 0; i < grid.GetLength(0); i++) {
            for (int k = 0; k < grid.GetLength(1); k++) {
                newGrid[i, k] = new Tile(grid[i, k].taken, grid[i, k].obj, grid[i, k].tileType, false);
            }
        }

        //checks and replace newGrids tiles
        for (int i = 0; i < smoothness; i++) {
            for (int x = 0; x < grid.GetLength(0); x++) {
                for (int y = 0; y < grid.GetLength(1); y++) {
                    if (x - 1 < 0 || x + 1 >= grid.GetLength(0) ||
                            y - 1 < 0 || y + 1 >= grid.GetLength(1)) {
                        continue;
                    }

                    if (grid[x, y].taken == 0) {
                        continue;
                    }

                    //Remove floating tiles and dead ends
                    if (NeighbourCount(new Vector2Int(x, y)) <= 1 &&
                            (grid[x, y].tileType == Tile.TileType.DOORWAY || grid[x, y].tileType == Tile.TileType.CORRIDOR)) {
                        RemoveTile(new Vector2Int(x, y), newGrid);
                    }

                    if (x - 2 < 0 || x + 2 >= grid.GetLength(0) ||
                            y - 2 < 0 || y + 2 >= grid.GetLength(1)) {
                        continue;
                    }

                    //remove doorways too close to eachother
                    if (grid[x, y].taken == 1 && grid[x, y].tileType == Tile.TileType.DOORWAY) {
                        if ((grid[x - 2, y].taken == 1 && grid[x - 2, y].tileType == Tile.TileType.DOORWAY)) {
                            RemoveTile(new Vector2Int(x - 2, y), newGrid);
                        }
                        if ((grid[x + 2, y].taken == 1 && grid[x + 2, y].tileType == Tile.TileType.DOORWAY)) {
                            RemoveTile(new Vector2Int(x + 2, y), newGrid);
                        }
                        if ((grid[x, y - 2].taken == 1 && grid[x, y - 2].tileType == Tile.TileType.DOORWAY)) {
                            RemoveTile(new Vector2Int(x, y - 2), newGrid);
                        }
                        if ((grid[x, y + 2].taken == 1 && grid[x, y + 2].tileType == Tile.TileType.DOORWAY)) {
                            RemoveTile(new Vector2Int(x, y + 2), newGrid);
                        }
                    }

                    //remove random
                    if (grid[x, y].taken != 1 || grid[x, y].tileType != Tile.TileType.DOORWAY) continue;
                    if (Random.value < 0.45f) {
                        RemoveTile(new Vector2Int(x, y), newGrid);
                    }
                }
            }
        }

        //overwrite grid with newGrid
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                grid[x, y] = new Tile(newGrid[x, y].taken, newGrid[x, y].obj, newGrid[x, y].tileType, false);
            }
        }
    }

    private static void RemoveTile(Vector2Int pos, Tile[,] newGrid) {
        newGrid[pos.x, pos.y].taken = 0;
        newGrid[pos.x, pos.y].tileType = Tile.TileType.NONE;
        GameObject g = newGrid[pos.x, pos.y].obj;
        newGrid[pos.x, pos.y].obj = null;
        if(g != null) DestroyImmediate(g);
    }

    private void PlacePlayer() {
        if (spawnedPrefabs == null || spawnedPrefabs.Count <= 0) return;
        GameObject selected = spawnedPrefabs[Random.Range(0, spawnedPrefabs.Count)];
        Vector3 pos = selected.transform.position + new Vector3(0f, 1.3f, 0f);
        player.position = pos;
    }
    
    private void SpawnPrefabs() {
        spawnedPrefabs.Clear();
        prefabWrapper = new GameObject("prefab_wrapper");
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (grid[x, y].taken != 1 || grid[x, y].tileType != Tile.TileType.CORRIDOR ||
                    NeighbourCount(new Vector2Int(x, y)) != 1) continue;
                Vector3 pos = grid[x, y].obj.transform.position;
                GameObject newPrefab = Instantiate(prefabs[0], prefabWrapper.transform, false);
                newPrefab.transform.position = pos;
                spawnedPrefabs.Add(newPrefab);
            }
        }
    }

    private void RotatePrefabs() {
        if (spawnedPrefabs == null || spawnedPrefabs.Count <= 0) return;

        foreach(GameObject spawnedPrefab in spawnedPrefabs) {
            Vector3[] directions = { transform.forward, -transform.forward, -transform.right, transform.right };
            List<Vector3> possibleDirections = new List<Vector3>();
            foreach (Vector3 direction in directions) {
                if (!Physics.Raycast(spawnedPrefab.transform.position, direction, out RaycastHit hit, 1f)) continue;
                if (direction == transform.forward) {
                    possibleDirections.Add(new Vector3(90, 0, 180));
                }
                if (direction == -transform.forward) {
                    possibleDirections.Add(new Vector3(90, 0, 0));
                }
                if (direction == transform.right) {
                    possibleDirections.Add(new Vector3(90, 0, 90));
                }
                if (direction == -transform.right) {
                    possibleDirections.Add(new Vector3(90, 0, -90));
                }
            }
            spawnedPrefab.transform.eulerAngles = possibleDirections[Random.Range(0, possibleDirections.Count)];
        }
    }

    private void SpawnEnemies() {
        GameObject enemyWrapper = new GameObject("enemy_wrapper");
        enemyWrapper.transform.SetParent(transform, false);
        for (int i = 0; i < possibleSpawnPoints.Count; i++) {
            float dist = Vector2.Distance(possibleSpawnPoints[i], player.position);
            if(dist < 40f) {
                possibleSpawnPoints.Remove(possibleSpawnPoints[i]);
            }
        }

        for (int i = 0; i < enemySpawnCount; i++) {
            Vector3 pos = possibleSpawnPoints[Random.Range(0, possibleSpawnPoints.Count)];
            GameObject enemy = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];

            if (enemy == null || pos == null) continue;
            GameObject newEnemy = Instantiate(enemy, enemyWrapper.transform, false);
            newEnemy.transform.position = pos;
            newEnemy.transform.eulerAngles = new Vector3(0, Random.Range(-180f, 180f), 0);
            newEnemy.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        }
    }

    private void ScaleMap(float factor) {
        transform.transform.localScale = new Vector3(factor, factor, factor);
    }

    #region FLOOD_FILL
    private Tile[,] highestGrid = null;
    private int highestSection = 0;

    private void FloodFill() {
        //create highest grid
        highestGrid = new Tile[grid.GetLength(0), grid.GetLength(1)];
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                highestGrid[x, y] = new Tile(0, null, Tile.TileType.NONE, false);
            }
        }

        //flood fill
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                if (grid[x, y].taken == 1 && !grid[x, y].filled)
                    FloodNode(x, y);
            }
        }

        //replace current grid with highest grid from floodfill
        for (int x = 0; x < grid.GetLength(0); x++) {
            for (int y = 0; y < grid.GetLength(1); y++) {
                grid[x, y] = new Tile(highestGrid[x, y].taken, highestGrid[x, y].obj, highestGrid[x, y].tileType, highestGrid[x, y].filled);
            }
        }
    }

    private void FloodNode(int x, int y) {
        Stack<Vector2Int> q = new Stack<Vector2Int>();
        q.Push(new Vector2Int(x, y));

        //create current grid
        Tile[,] currentGrid = new Tile[grid.GetLength(0), grid.GetLength(1)]; ;
        int currentSection = 0;
        for (int i = 0; i < grid.GetLength(0); i++) {
            for (int k = 0; k < grid.GetLength(1); k++) {
                currentGrid[i, k] = new Tile(0, null, Tile.TileType.NONE, false);
            }
        }

        while (q.Count > 0) {
            Vector2Int n = q.Pop();
            if (grid[n.x, n.y].taken != 1 || grid[n.x, n.y].filled) continue;
            grid[n.x, n.y].filled = true;
            currentSection++;
            currentGrid[n.x, n.y] = new Tile(grid[n.x, n.y].taken, null, grid[n.x, n.y].tileType, grid[n.x, n.y].filled);
            if(n.x + 1 < grid.GetLength(0)) q.Push(new Vector2Int(n.x + 1, n.y));
            if (n.x - 1 > 0) q.Push(new Vector2Int(n.x - 1, n.y));
            if (n.y + 1 < grid.GetLength(1)) q.Push(new Vector2Int(n.x, n.y + 1));
            if (n.y - 1 > 0) q.Push(new Vector2Int(n.x, n.y - 1));
        }

        //replace the highestSection with the new highest
        if(currentSection > highestSection) {
            highestSection = currentSection;
            for (int i = 0; i < currentGrid.GetLength(0); i++) {
                for (int k = 0; k < currentGrid.GetLength(1); k++) {
                    highestGrid[i, k] = new Tile(currentGrid[i, k].taken, null, currentGrid[i, k].tileType, currentGrid[i, k].filled);
                }
            }
        }
    }
    #endregion
}