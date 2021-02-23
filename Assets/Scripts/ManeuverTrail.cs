using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;


/// <summary>
/// GPUトレイル
/// </summary>
public sealed class ManeuverTrail : MonoBehaviour {
    #region DEFINE
    private static class CSPARAM {
        public static readonly string CALC_TRAIL = "CalcTrail";

        public static readonly int SPLIT = Shader.PropertyToID("_Split");
        public static readonly int LIFE = Shader.PropertyToID("_Life");
        public static readonly int TIME = Shader.PropertyToID("_CheckTime");
        public static readonly int DELTA_TIME = Shader.PropertyToID("_DeltaTime");
        public static readonly int UPDATE_DISTANCE_MIN = Shader.PropertyToID("_UpdateDistanceMin");
        public static readonly int TRAIL_NUM = Shader.PropertyToID("_TrailNum");
        public static readonly int NODE_NUM_PER_TRAIL = Shader.PropertyToID("_NodeNumPerTrail");
        public static readonly int TRAIL_BUFFER = Shader.PropertyToID("_TrailBuffer");
        public static readonly int NODE_BUFFER = Shader.PropertyToID("_NodeBuffer");
        public static readonly int INPUT_BUFFER = Shader.PropertyToID("_InputBuffer");
        public static readonly int THICKNESS = Shader.PropertyToID("_Thickness");
    }

    /// <summary>
    /// トレイルデータ（※ComputeShader内の定義とレイアウトを合わせる）
    /// </summary>
    public struct Trail {
        public int currentNodeIdx;
        //public Color color; // ポリゴン確認用
    }

    /// <summary>
    /// トレイル制御点データ（※ComputeShader内の定義とレイアウトを合わせる）
    /// </summary>
    public struct Node {
        public float time;
        public Vector3 position;
        public Vector3 direct;
        public float speed;
    }

    /// <summary>
    /// 入力データ（※ComputeShader内の定義とレイアウトを合わせる）
    /// </summary>
    public struct Input {
        public int enable;
        public Vector3 position;
        public Vector3 direct;
        public float speed;
    }

    private const float MAX_FPS = 60f;  // 最大対応fps
    #endregion


    #region MEMBER
    public ComputeShader csTrail = null;// トレイル用コンピュートシェーダ
    public Material material = null;    // トレイル用マテリアル
    public Material materialForMesh = null; // トレイル用マテリアル

    public int trailNum = 1;            // トレイル最大数（今回はミサイル最大数と同義）
    public int split = 1;               // 補完点数（割と重いのとジグザク軌道と相性悪いかも）
    public float life = 10f;            // 制御点生存時間（トレイルの表示時間）
    public float updateDistaceMin = 0.01f;  // 制御点最小間隔（トレイル発生最大距離）
    public float inertia = 0.1f;        // 慣性強度（大きいほどトレイルが流れ、ぐちゃぐちゃになりやすい）
    [Range(0.01f, 1f)]
    public float thickness = 0.25f;     // 太さ

    private int kernelTrail = -1;       // ComputeShaderのTrail処理のカーネルNo.
    private int threadGroup = 0;        // スレッドグループキャッシュ
    private int nodeNum = 0;            // トレイル制御点数
    private int totalNodeNum = 0;       // 全トレイルの合計ノード数
    private ComputeBuffer trailBuffer = null;   // トレイルとその先頭情報バッファ
    private ComputeBuffer nodeBuffer = null;    // 全トレイルの制御点バッファ
    private ComputeBuffer inputBuffer = null;   // 全トレイルの制御点バッファ

    private float[] checkTimeArray = null;  // 有効時間配列
    private Trail[] initTrailArray = null;  // トレイルバッファの初期化配列
    private Node[] initNodeArray = null;    // ノードバッファの初期化配列
    private Input[] initInputArray = null;  // 入力バッファの初期化配列
    private Input[] inputArray = null;      // 入力バッファ
#if DRAW_MESH || UNITY_STANDALONE_OSX
    private Mesh mesh;
    private NativeArray<Vector3> vertices;
    private NativeArray<int> indicies;
    private NativeArray<Trail> trailArray;  // データチェック用
    private NativeArray<Node> nodeArray;    // データチェック用
#endif
    #endregion


    #region MAIN FUNCTION
    /// <summary>
    /// 生成処理
    /// </summary>
    void Awake() {
        this.nodeNum = Mathf.CeilToInt(life * MAX_FPS) * this.split;
        this.totalNodeNum = this.trailNum * this.nodeNum;
        this.checkTimeArray = new float[this.trailNum];
        this.initTrailArray = new Trail[this.trailNum];
        this.initNodeArray = new Node[this.totalNodeNum];
        this.initInputArray = new Input[this.trailNum];
        this.inputArray = new Input[this.trailNum];
#if DRAW_MESH || UNITY_STANDALONE_OSX
        this.trailArray = new NativeArray<Trail>(this.trailNum, Allocator.Persistent);
        this.nodeArray = new NativeArray<Node>(this.totalNodeNum, Allocator.Persistent);
        this.vertices = new NativeArray<Vector3>(this.trailNum * this.nodeNum * BurstGeometry.cols, Allocator.Persistent);
        this.indicies = new NativeArray<int>(this.trailNum * (this.nodeNum - 1) * BurstGeometry.quadVtx * BurstGeometry.cols, Allocator.Persistent);
        unsafe {
            BurstGeometry.CreateIndexBuffer(this.trailNum, this.nodeNum, (int*)indicies.GetUnsafeReadOnlyPtr());
            //BurstGeometry.CreateIndexBuffer(this.trailNum, this.nodeNum, indicies); // Debug用
        }
#endif

        for (int i = 0; i < this.trailNum; ++i) {
            Trail tr = new Trail { currentNodeIdx = -this.split };
            this.initTrailArray[i] = tr;
        }
        var initNode = new Node() { time = -1 };
        for (int i = 0; i < this.totalNodeNum; ++i)
            this.initNodeArray[i] = initNode;

        this.trailBuffer = new ComputeBuffer(this.trailNum, Marshal.SizeOf(typeof(Trail)));
        this.nodeBuffer = new ComputeBuffer(this.totalNodeNum, Marshal.SizeOf(typeof(Node)));
        this.inputBuffer = new ComputeBuffer(this.trailNum, Marshal.SizeOf(typeof(Input)));

        this.Clear();

        this.csTrail.SetInt(CSPARAM.SPLIT, this.split);
        this.csTrail.SetInt(CSPARAM.TRAIL_NUM, this.trailNum);
        this.csTrail.SetInt(CSPARAM.NODE_NUM_PER_TRAIL, this.nodeNum);
        this.csTrail.SetFloat(CSPARAM.LIFE, this.life);
        this.csTrail.SetFloat(CSPARAM.UPDATE_DISTANCE_MIN, this.updateDistaceMin);

        this.kernelTrail = this.csTrail.FindKernel(CSPARAM.CALC_TRAIL);
        this.csTrail.SetBuffer(this.kernelTrail, CSPARAM.TRAIL_BUFFER, this.trailBuffer);
        this.csTrail.SetBuffer(this.kernelTrail, CSPARAM.NODE_BUFFER, this.nodeBuffer);
        this.csTrail.SetBuffer(this.kernelTrail, CSPARAM.INPUT_BUFFER, this.inputBuffer);

        uint x, y, z;
        this.csTrail.GetKernelThreadGroupSizes(this.kernelTrail, out x, out y, out z);
        this.threadGroup = Mathf.CeilToInt((float)this.trailNum / x);

#if !DRAW_MESH && !UNITY_STANDALONE_OSX
        this.material.SetInt(CSPARAM.NODE_NUM_PER_TRAIL, this.nodeNum);
        this.material.SetBuffer(CSPARAM.TRAIL_BUFFER, this.trailBuffer);
        this.material.SetBuffer(CSPARAM.NODE_BUFFER, this.nodeBuffer);
        this.material.SetFloat(CSPARAM.LIFE, this.life);
#endif
    }

#if DRAW_MESH || UNITY_STANDALONE_OSX
    private void Start() {
        // NOTE:
        // 本来はバッファ含めてComputeShaderでやるべきだが
        // 元々GPUでやろうとした本質が変わってしまうのでComputeShaderの変更なしでCPUから生成する

        // Meshの生成
        this.mesh = new Mesh();
        this.mesh.name = "Trail Mesh";
        this.mesh.indexFormat = IndexFormat.UInt32; // 65535頂点を超えた場合の対応
        this.mesh.SetVertices(this.vertices);
        this.mesh.SetIndices(this.indicies, MeshTopology.Triangles, 0);

        //// MeshRendererの生成
        //var renderer = this.gameObject.AddComponent<MeshRenderer>();
        //renderer.sharedMaterial = this.materialForJob;
        //renderer.shadowCastingMode = ShadowCastingMode.Off;
        //renderer.receiveShadows = false;
        //renderer.lightProbeUsage = LightProbeUsage.Off;
        //renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        //renderer.allowOcclusionWhenDynamic = false;

        //// MeshFilterの生成
        //var filter = this.gameObject.AddComponent<MeshFilter>();
        //filter.mesh = this.mesh;
    }
#endif

    /// <summary>
    /// 破棄
    /// </summary>
    void OnDestroy() {
#if DRAW_MESH || UNITY_STANDALONE_OSX
        this.trailArray.Dispose();
        this.nodeArray.Dispose();
        this.vertices.Dispose();
        this.indicies.Dispose();
        Object.Destroy(this.mesh);
#else
        this.material.SetFloat(CSPARAM.TIME, 0f);
#endif
        this.trailBuffer.Release();
        this.nodeBuffer.Release();
        this.inputBuffer.Release();
        this.trailBuffer = this.nodeBuffer = this.inputBuffer = null;
    }

    /// <summary>
    /// 更新
    /// ミサイル挙動の後で発行したいのでLateUpdate
    /// </summary>
    void LateUpdate() {
        float time = Time.time;
        float deltaTime = Time.deltaTime;

        this.inputBuffer.SetData(this.inputArray);
        this.csTrail.SetFloat(CSPARAM.TIME, time); // シェーダー内の_TimeとTime.timeは一致しないのでCPU側の時間で統一する
        this.csTrail.SetFloat(CSPARAM.DELTA_TIME, deltaTime); // Time.timeで管理するのでdeltaTimeを渡す
        this.csTrail.Dispatch(this.kernelTrail, this.threadGroup, 1, 1);

#if !DRAW_MESH && !UNITY_STANDALONE_OSX
        ////------------------------------------------------------------
        //// NOTE: 毎フレーム渡す必要はないが調整用
        //this.csTrail.SetFloat(CSPARAM.UPDATE_DISTANCE_MIN, this.updateDistaceMin);
        //this.csTrail.SetFloat(CSPARAM.LIFE, this.life);
        //this.material.SetFloat(CSPARAM.LIFE, this.life);
        //this.material.SetFloat(CSPARAM.THICKNESS, this.thickness));
        ////------------------------------------------------------------

        this.material.SetFloat(CSPARAM.TIME, time);
#endif
    }

#if DRAW_MESH || UNITY_STANDALONE_OSX
    /// <summary>
    /// 描画前処理
    /// LateUpdateでやってもいいがGPU同期待ちするので小細工
    /// </summary>
    private void OnRenderObject() {
        // 非効率な設計だけどGPUの演算結果をCPU側に取り出す
        AsyncGPUReadback.RequestIntoNativeArray(ref this.trailArray, this.trailBuffer).WaitForCompletion();
        AsyncGPUReadback.RequestIntoNativeArray(ref this.nodeArray, this.nodeBuffer).WaitForCompletion();

        int activeTrail = 0;
        unsafe {
            activeTrail = BurstGeometry.UpdateGeometry(this.trailNum, this.nodeNum,
                this.thickness,
                Time.time,
                this.life,
                (Trail*)this.trailArray.GetUnsafeReadOnlyPtr(),
                (Node*)this.nodeArray.GetUnsafeReadOnlyPtr(),
                (Vector3*)this.vertices.GetUnsafeReadOnlyPtr());
            //this.trailArray, this.nodeArray, this.vertices); // Debug用
        }

        this.mesh.SetVertices(this.vertices);
        if (activeTrail > 0) {
            // 必要トレイル数分に制限する
            int activeIndecies = activeTrail * (this.nodeNum - 1) * BurstGeometry.quadVtx * BurstGeometry.cols;
            SubMeshDescriptor desc = new SubMeshDescriptor(0, activeIndecies, MeshTopology.Triangles);
            mesh.SetSubMesh(0, desc, MeshUpdateFlags.Default);
            this.mesh.RecalculateBounds();
            this.materialForMesh.SetPass(0);
            Graphics.DrawMeshNow(this.mesh, Matrix4x4.identity);
        }
    }
#else
    /// <summary>
    /// 描画
    /// </summary>
    private void OnRenderObject() {
        ////内部バッファ確認用
        //AsyncGPUReadback.RequestIntoNativeArray(ref this.trailArray, this.trailBuffer).WaitForCompletion();
        //AsyncGPUReadback.RequestIntoNativeArray(ref this.nodeArray, this.nodeBuffer).WaitForCompletion();
        ////this.trailBuffer.GetData(this.trailArray);
        ////this.nodeBuffer.GetData(this.nodeArray);
        //int trailIdx = this.trailArray[0].currentNodeIdx;

        // NOTE: 描画タイミングを制御する場合はCommandBufferを使う、ただしSceneViewに出なくなるので開発中はオススメしない
        this.material.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Points, this.nodeNum, this.trailNum);
    }
#endif
    #endregion


    #region PUBLIC FUNCTION
    /// <summary>
    /// 開始点
    /// </summary>
    /// <param name="no">トレイルNo.</param>
    /// <param name="top">先頭座標</param>
    /// <param name="velocity">移動速度</param>
    public void StartTrail(int no, Vector3 top, Vector3 velocity, float inertiaRatio = 1f) {
        Debug.Assert(no >= 0 && no < this.trailNum, "TRAIL NO. ERROR.......... : " + no.ToString());
        this.checkTimeArray[no] = this.life;
        this.inputArray[no].enable = 2;
        this.inputArray[no].position = top;
        this.inputArray[no].direct = Vector3.Normalize(velocity);
        this.inputArray[no].speed = Vector3.Magnitude(velocity) * (this.inertia * inertiaRatio);
    }

    /// <summary>
    /// トレイルの更新
    /// </summary>
    /// <param name="no">トレイルNo.</param>
    /// <param name="top">先頭座標</param>
    /// <param name="velocity">移動速度</param>
    /// <param name="inertiaRatio">慣性倍率</param>
    public void UpdateTrail(int no, Vector3 top, Vector3 velocity, float inertiaRatio = 1f) {
        Debug.Assert(no >= 0 && no < this.trailNum);
        this.checkTimeArray[no] = this.life;
        this.inputArray[no].enable = 1;
        this.inputArray[no].position = top;
        this.inputArray[no].direct = Vector3.Normalize(velocity);
        this.inputArray[no].speed = Vector3.Magnitude(velocity) * (this.inertia * inertiaRatio);
    }

    /// <summary>
    /// トレイル更新の停止
    /// </summary>
    /// <param name="no">トレイルNo.</param>
    public void StopTrail(int no) {
        Debug.Assert(no >= 0 && no < this.trailNum);
        this.inputArray[no].enable = 0;
    }

    /// <summary>
    /// 消去
    /// </summary>
    public void Clear() {
        this.trailBuffer.SetData(this.initTrailArray);
        this.nodeBuffer.SetData(this.initNodeArray);
        this.inputBuffer.SetData(this.initInputArray);

        for (int i = 0; i < this.trailNum; ++i) {
            this.checkTimeArray[i] = 0f;
            this.inputArray[i] = this.initInputArray[i];
        }
    }

    /// <summary>
    /// 消去
    /// </summary>
    /// <param name="no">トレイルNo.</param>
    public void Clear(int no) {
        this.inputArray[no].enable = 0;
    }
    #endregion
}
