using UnityEngine;


/// <summary>
/// 高機動ミサイル
/// </summary>
public struct ManuverMissle {
	#region DEFINE
	private const float FRAME_TIME = 0.016667f;

	private const int   WINDING_MAX = 2;     // 射出急制動回数
	private const float POWER_EDGE_TIME = FRAME_TIME * 4f; // 急制動遷移時間
	//private const float MISSILE_LENGTH = 1f; // ミサイルの長さ
	private const float MISSILE_LENGTH = 0f; // ミサイルの長さ
	private const float HIT_RANGE = 3f;
	#endregion


	#region PROPERTY
	/// <summary>
	/// 行列取得
	/// </summary>
	public Matrix4x4 matrix { get { return Matrix4x4.TRS(this.position, this.rotation, Vector3.one); } }

	/// <summary>
	/// 生存中か
	/// </summary>
	public bool active { get; private set; }
	#endregion


	#region MEMBER
	private Transform target;      // 目標

	private Vector3 position;      // 現在地
	private Quaternion rotation;   // 向き

	private Vector3 direct;        // 移動方向
	private float speed;           // 移動速度
	private float torqueLevel;     // 旋回力（バネ係数）
	private float damper;          // 旋回減衰力（弱すぎるとブレる、強すぎると動きが重い）
	private Vector3 omega;         // 角速度
	
	private bool powerEnable;
	private Vector3 force;         // 外力方向
	private float power, powerMax; // 外力
	private float powerTime;       // 急制動時間
	private float powerEnableTime; // 急制動制御時間
	private int powerCount;        // 急制動回数
	
	private MissileParam param;

	private ManuverTrail trail;  // トレイルエフェクト
	private float inertiaRatio;  // 慣性倍率

	private int index;    // 稼働No.（トレイル用）
	
	private float shiftSpeedTime;
	private float shiftSpeedStart;
	#endregion


	#region MAIN FUNCTION
	/// <summary>
	/// 初期化
	/// </summary>
	/// <param name="index">ミサイルNo.</param>
	/// <param name="trail">トレイルエフェクト</param>
	public void Initialize(int index, ManuverTrail trail) {
		this.index = index;
		this.trail = trail;
	}

	/// <summary>
	/// 派生クラスでの固有更新処理(falseを返すと消滅)
	/// </summary>
	/// <param name="elapsedTime">経過時間</param>
	public void Proc(float elapsedTime) {
		// コマ送りチェック用
		elapsedTime = FRAME_TIME;

		// 急制動
		this.PowerProc(elapsedTime);
		// 移動
		bool end = this.ProcTorque(elapsedTime);
		// トレイル反映
		Vector3 move = this.direct * this.speed; // 慣性の速度
		Vector3 point = this.position - this.direct * MISSILE_LENGTH; // ミサイルのケツの位置にトレイルをつける
		this.trail.UpdateTrail(this.index, point, move, this.inertiaRatio);
		
		if (end)
			this.Vanish();
	}
	#endregion


	#region PUBLIC FUNCTION
	/// <summary>
	/// 射撃
	/// </summary>
	/// <param name="position">発射座標</param>
	/// <param name="direct">射出方向</param>
	/// <param name="target">目標</param>
	/// <param name="param">ミサイル共通情報</param>
	public void Shoot(Vector3 position, Vector3 direct, Transform target, MissileParam param) {
		this.active = true;

		this.param = param;
		
		// 移動パラメータ
		this.target = target;
		//this.direct = Vector3.Normalize(direct);
		this.direct = Vector3.Slerp(Vector3.forward, direct, param.shootDirect);
		this.speed = param.startSpeed;
		
		// 加速パラメータ
		this.shiftSpeedTime = 0f;
		this.shiftSpeedStart = this.speed;

		// 急制動パラメータ
		this.force = direct;
		this.powerCount = 0;
		this.powerEnable = true;
		this.powerTime = 0f; // 急制動開始までのディレイ
		this.powerEnableTime = FRAME_TIME * Random.Range(param.shootFrame.x, param.shootFrame.y);
		this.power = 0f;
		this.powerMax = Random.Range(param.shoot.x, param.shoot.y);

		// 旋回パラメータ
		this.torqueLevel = 15f;//Random.Range(8f, 12f); // 旋回力のばらし
		this.damper = 5f;//Random.Range(3f, 5f); // 旋回減衰のばらし
		this.omega = Vector3.zero;

		// 初期座標
		this.position = position;
		this.rotation = Quaternion.FromToRotation(Vector3.forward, this.direct);
		Vector3 point = this.position - this.direct * MISSILE_LENGTH; // ミサイルのケツの位置にトレイルをつける
		// トレイル開始
		this.trail.StartTrail(this.index, point, this.direct * speed, 0f);
		this.inertiaRatio = 1f;
	}

	/// <summary>
	/// 消滅
	/// </summary>
	public void Vanish() {
		this.active = false;
		if (this.trail != null)
			this.trail.StopTrail(this.index);
	}
	#endregion


	#region PRIVATE FUNCTION
	/// <summary>
	/// 急制動発動
	/// </summary>
	private void PowerEmit() {
		Vector3 targetDir = Vector3.Normalize(this.target.position - this.position);

		// 射出時の急制動
		Vector3 newForce;
		int check = 0;
		do {
			// 急制動方向の抽選
			newForce.x = Random.Range(100f, 130f) * (Random.value < 0.5f ? -1f : 1f);
			newForce.y = Random.Range(100f, 130f) * (Random.value < 0.5f ? -1f : 1f);
			newForce.z = 0f;

			// 著しく目標方向と違う方向へ飛ばさない補正
			float targetAngle = Vector3.Angle(targetDir, this.direct);
			if (targetAngle > 90f)
				newForce.z = Mathf.Lerp(200f, 400f, Mathf.Clamp01((targetAngle - 30f) / 30f));

			Quaternion rot = Quaternion.FromToRotation(Vector3.forward, targetDir);
			newForce = Vector3.Normalize(rot * newForce);
				
			// 無限ループ対策の抽選限界
			if (++check >= 10)
				break;

			// 前回と一定角度以内しか変わらない場合は再抽選
		} while (Vector3.Angle(this.force, newForce) < 60f);
		this.force = newForce;

		this.power = 0f;
		this.powerMax = Random.Range(param.power.x, param.power.y);
		this.powerTime = 0f;
		this.powerEnableTime = FRAME_TIME * Random.Range(param.powerFrame.x, param.powerFrame.y);
		this.powerEnable = true;

		this.powerCount++;
	}

	/// <summary>
	/// 急制動制御
	/// </summary>
	/// <param name="elapsedTime"></param>
	private void PowerProc(float elapsedTime) {
		if (!this.powerEnable)
			return;

		//bool first = (this.powerTime < 0f);

		this.powerTime += elapsedTime;
		// ディレイ対応
		if (this.powerTime < 0f)
			return;

		//if (first)
		//	this.PowerEmit();

		if (this.powerTime < this.powerEnableTime) {
			float t = 0f;
			if (this.powerTime < POWER_EDGE_TIME) {
				// 開始
				t = this.powerTime / POWER_EDGE_TIME;
				this.power = EaseInQuad(0f, this.powerMax, t);
			} else if (this.powerTime < (this.powerEnableTime - POWER_EDGE_TIME)) {
				// 有効
				this.power = this.powerMax;
			} else {
				// 終了
				t = (this.powerTime - (this.powerEnableTime - POWER_EDGE_TIME)) / POWER_EDGE_TIME;
				this.power = EaseOutQuad(this.powerMax, 0f, t);
			}
		} else {
			this.power = 0f;

			if (this.powerCount < WINDING_MAX) {
				this.PowerEmit(); // もう一度
			} else {
				this.powerEnable = false;
			}
		}

		this.position += this.force * (this.power * elapsedTime);
	}
	
	/// <summary>
	/// 追尾制御（バネトルク）
	/// </summary>
	/// <param name="elapsedTime">経過時間</param>
	/// <returns>終了した</returns>
	private bool ProcTorque(float elapsedTime) {
		// バネトルク
		var targetpos = this.target.position;
		var diff = targetpos - this.position;
		//var up = transform.TransformVector(Vector3.up);
		var up = this.rotation * Vector3.up;
		var targetrot = Quaternion.LookRotation(diff, up);
		var myrot = this.rotation;
		var myrotInv = new Quaternion(-myrot.x, -myrot.y, -myrot.z, myrot.w); // Quaternion.Inverse
		var rot = targetrot * myrotInv;
		if (rot.w < 0f) {
			rot.x = -rot.x;
			rot.y = -rot.y;
			rot.z = -rot.z;
		}
		var torque = new Vector3(rot.x, rot.y, rot.z);

		// 角速度の更新
		var dt = elapsedTime;
		this.omega += torque * (this.torqueLevel * dt);
		this.omega -= this.omega * (this.damper * dt); // 角速度減衰
		// 旋回
		var nx = this.omega.x * dt;
		var ny = this.omega.y * dt;
		var nz = this.omega.z * dt;
		var len2 = nx * nx + ny * ny + nz * nz; // sin^2
		var w = Mathf.Sqrt(1f - len2); // (sin^2 + cos^2) = 1
		Quaternion q = new Quaternion(nx, ny, nz, w);
		this.rotation = q * myrot;
		this.rotation.Normalize(); // NOTE: 小数点誤差対策

		// 進行方向の更新
		this.direct = this.rotation * Vector3.forward;
		// 加速
		if (this.shiftSpeedTime < this.param.accelTime) {
			this.shiftSpeedTime += elapsedTime;
			var t = Mathf.Clamp01(this.shiftSpeedTime / this.param.accelTime);
			this.speed = this.EaseInQuad(this.shiftSpeedStart, this.param.maxSpeed, t);
		} else {
			this.speed = this.param.maxSpeed;
		}
		// 座標更新
		this.position += this.direct * (this.speed * elapsedTime);
		// 接触判定（仮）
		if ((this.target.position - this.position).magnitude < HIT_RANGE)
			return true;
		return false;
	}

	/// <summary>
	/// イージング補間(2次)
	/// </summary>
	/// <param name="p">始点</param>
	/// <param name="q">終点</param>
	/// <param name="t">位置(0.0f～1.0f)</param>
	public float EaseInQuad(float p, float q, float t) {
		return (q - p) * t * t + p;
	}
	public float EaseOutQuad(float p, float q, float t) {
		return -(q - p) * t * (t - 2.0f) + p;
	}
	#endregion
}