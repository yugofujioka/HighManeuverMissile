using System.Runtime.InteropServices;
using UnityEngine;


/// <summary>
/// GPUトレイル
/// </summary>
public sealed class ManuverTrail : MonoBehaviour {
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

	public int trailNum = 1;            // トレイル最大数（今回はミサイル最大数と同義）
	public int split = 1;				// 補完点数（割と重いのとジグザク軌道と相性悪いかも）
	public float life = 10f;            // 制御点生存時間（トレイルの表示時間）
	public float updateDistaceMin = 0.01f;  // 制御点最小間隔（トレイル発生最大距離）
	public float inertia = 0.1f;        // 慣性強度（大きいほどトレイルが流れ、ぐちゃぐちゃになりやすい）

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
	//private Trail[] trailArray = null;	// データチェック用
	//private Node[] nodeArray = null;		// データチェック用
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
		//this.trailArray = new Trail[this.trailNum];
		//this.nodeArray = new Node[this.totalNodeNum];

		for (int i = 0; i < this.trailNum; ++i) {
			Trail tr = new Trail();
			tr.currentNodeIdx = -this.split;
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

		this.material.SetInt(CSPARAM.NODE_NUM_PER_TRAIL, this.nodeNum);
		this.material.SetBuffer(CSPARAM.TRAIL_BUFFER, this.trailBuffer);
		this.material.SetBuffer(CSPARAM.NODE_BUFFER, this.nodeBuffer);
		this.material.SetFloat(CSPARAM.LIFE, this.life);
	}

	/// <summary>
	/// 破棄
	/// </summary>
	void OnDestroy() {
		this.material.SetFloat(CSPARAM.TIME, 0f);
		this.trailBuffer.Release();
		this.nodeBuffer.Release();
		this.inputBuffer.Release();
		this.trailBuffer = this.nodeBuffer = this.inputBuffer = null;
	}

	/// <summary>
	/// 更新
	/// </summary>
	void LateUpdate() {
		float time = Time.time;
		float deltaTime = Time.deltaTime;

		this.inputBuffer.SetData(this.inputArray);
		this.csTrail.SetFloat(CSPARAM.TIME, time); // シェーダー内の_TimeとTime.timeは一致しないのでCPU側の時間で統一する
		this.csTrail.SetFloat(CSPARAM.DELTA_TIME, deltaTime); // Time.timeで管理するのでdeltaTimeを渡す
		this.csTrail.Dispatch(this.kernelTrail, this.threadGroup, 1, 1);
		
		//------------------------------------------------------------
		// NOTE: 毎フレーム渡す必要はないが調整用
		this.csTrail.SetFloat(CSPARAM.UPDATE_DISTANCE_MIN, this.updateDistaceMin);
		this.csTrail.SetFloat(CSPARAM.LIFE, this.life);
		this.material.SetFloat(CSPARAM.LIFE, this.life);
		//------------------------------------------------------------

		this.material.SetFloat(CSPARAM.TIME, time);
	}

	/// <summary>
	/// 描画
	/// </summary>
	private void OnRenderObject() {
		//内部バッファ確認用
		//this.trailBuffer.GetData(this.trailArray);
		//int trailIdx = this.trailArray[0].currentNodeIdx;
		//this.nodeBuffer.GetData(this.nodeArray);

		// NOTE: 描画タイミングを制御する場合はCommandBufferを使う、ただしSceneViewに出なくなるので開発中はオススメしない
		this.material.SetPass(0);
		Graphics.DrawProcedural(MeshTopology.Points, this.nodeNum, this.trailNum);
	}
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
