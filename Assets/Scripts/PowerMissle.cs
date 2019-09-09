using UnityEngine;


/// <summary>
/// 急制動ミサイル
/// </summary>
public class PowerMissle : MonoBehaviour {
	#region DEFINE
	private const float FRAME_TIME = 0.016667f;

	private const int   WINDING_MAX = 2;     // 射出急制動回数
	private const float POWER_EDGE_TIME = FRAME_TIME * 4f; // 急制動遷移時間
	private const float HIT_RANGE = 2f;
	#endregion


	#region MEMBER
	public int index;
	public ManeuverTrail trail;  // トレイルエフェクト
	public Transform target;    // 目標
	public float startSpeed;    // 初速（発射直後の速度）
	public float maxSpeed;      // 最大速度（追従中の最大速度、非旋回中に加速がかかるので最大閾値）
	public float accelTime;     // 加速時間（小さいほど急加速する）
	public float torqueLevel;   // 旋回力（バネ係数）
	public float damper;        // 旋回減衰力（弱すぎるとブレる、強すぎると動きが重い）
	public Vector2 powerRange;  // 急制動力(x～yのランダム)
	public Vector2Int powerFrameRange; // 急制動力有効時間フレーム(x～yのランダム)
							   
	private Vector3 direct;     // 移動方向
	private float speed;        // 移動速度
	private Vector3 omega;      // 角速度
	private float shiftTime;    // 加速遷移時間
	
	private bool powerEnable;
	private Vector3 force;         // 外力方向
	private float power, powerMax; // 外力
	private float powerTime;       // 急制動時間
	private float powerEnableTime; // 急制動制御時間
	private int powerCount;        // 急制動回数
	#endregion


	#region MAIN FUNCTION
	public void Start() {
		direct = transform.rotation * Vector3.forward;
		speed = startSpeed;
		omega = Vector3.zero;
		shiftTime = 0f;

		// 急制動パラメータ
		force = direct;
		powerCount = 0;
		powerEnable = true;
		// NOTE: 急制動開始までのディレイ
		powerTime = FRAME_TIME * -Random.Range(15, 25);
		power = 0f;

		trail.StartTrail(index, transform.position, direct * speed, 0f);
	}

	public void Update() {
		float elapsedTime = Time.deltaTime;

		// 急制動
		PowerProc(elapsedTime);
		// 移動
		if (ProcTorque(elapsedTime))
			return;
		// 終了
		gameObject.SetActive(false);
	}
	#endregion


	#region PRIVATE FUNCTION
	/// <summary>
	/// 急制動発動
	/// </summary>
	private void PowerEmit() {
		Vector3 targetDir = target.position - transform.position;
		targetDir.Normalize();

		// 射出時の急制動
		Vector3 newForce;
		int check = 0;
		do {
			// 急制動方向の抽選
			newForce.x = Random.Range(100f, 130f);
			newForce.x *= (Random.value < 0.5f ? -1f : 1f);
			newForce.y = Random.Range(100f, 130f);
			newForce.y *= (Random.value < 0.5f ? -1f : 1f);
			newForce.z = 0f;

			// 著しく目標方向と違う方向へ飛ばさない補正
			float targetAngle = Vector3.Angle(targetDir, direct);
			if (targetAngle > 90f) {
				float t = Mathf.Clamp01((targetAngle - 30f) / 30f);
				newForce.z = Mathf.Lerp(200f, 400f, t);
			}

			Quaternion rot = Quaternion.FromToRotation(Vector3.forward, targetDir);
			newForce = Vector3.Normalize(rot * newForce);
				
			// 無限ループ対策の抽選限界
			if (++check >= 10)
				break;

			// 前回と一定角度以内しか変わらない場合は再抽選
		} while (Vector3.Angle(force, newForce) < 60f);

		force = newForce;

		power = 0f;
		powerMax = Random.Range(powerRange.x, powerRange.y);
		powerTime = 0f;
		powerEnableTime = FRAME_TIME * Random.Range(powerFrameRange.x, powerFrameRange.y);
		powerEnable = true;
		powerCount++;
	}

	/// <summary>
	/// 急制動制御
	/// </summary>
	/// <param name="elapsedTime"></param>
	private void PowerProc(float elapsedTime) {
		if (!powerEnable)
			return;

		bool first = (powerTime < 0f);

		powerTime += elapsedTime;
		// ディレイ対応
		if (powerTime < 0f)
			return;

		if (first)
			PowerEmit();

		if (powerTime < powerEnableTime) {
			float t = 0f;
			if (powerTime < POWER_EDGE_TIME) {
				// 開始
				t = powerTime / POWER_EDGE_TIME;
				power = EaseInQuad(0f, powerMax, t);
			} else if (powerTime < (powerEnableTime - POWER_EDGE_TIME)) {
				// 有効
				power = powerMax;
			} else {
				// 終了
				t = powerTime - (powerEnableTime - POWER_EDGE_TIME);
				t /= POWER_EDGE_TIME;
				power = EaseOutQuad(powerMax, 0f, t);
			}
		} else {
			power = 0f;

			if (powerCount < WINDING_MAX) {
				PowerEmit(); // もう一度
			} else {
				powerEnable = false;
			}
		}

		transform.position += force * (power * elapsedTime);
	}
	
	/// <summary>
	/// 追尾制御（バネトルク）
	/// </summary>
	/// <param name="elapsedTime">経過時間</param>
	/// <returns>終了した</returns>
	private bool ProcTorque(float elapsedTime) {
		// バネトルク
		var targetpos = target.position;
		var diff = targetpos - transform.position;
		var up = transform.TransformVector(Vector3.up);
		var targetrot = Quaternion.LookRotation(diff, up);
		var myrot = transform.rotation;
		var rot = targetrot * Quaternion.Inverse(myrot);
		if (rot.w < 0f) {
			rot.x = -rot.x;
			rot.y = -rot.y;
			rot.z = -rot.z;
		}
		var torque = new Vector3(rot.x, rot.y, rot.z);

		// 角速度の更新
		var dt = elapsedTime;
		omega += torque * (torqueLevel * dt);
		omega -= omega * (damper * dt);
		// 回転値の更新
		var r = omega * dt;
		var w = Mathf.Sqrt(1f - r.sqrMagnitude);
		var q = new Quaternion(r.x, r.y, r.z, w);
		transform.rotation = q * myrot;

		// 進行方向の更新
		direct = transform.rotation * Vector3.forward;
		// 加速
		if (shiftTime < accelTime) {
			shiftTime += elapsedTime;
			float t = Mathf.Clamp01(shiftTime / accelTime);
			speed = EaseInQuad(startSpeed, maxSpeed, t);
		} else {
			speed = maxSpeed;
		}
		// 座標更新
		var move = direct * speed;
		var position = transform.position + move * elapsedTime;
		transform.position = position;
		// トレイル更新
		trail.UpdateTrail(index, position, move);
		// 接触判定（仮）
		if (Vector3.Distance(target.position, transform.position) < HIT_RANGE) {
			trail.StopTrail(index);
			return false;
		}
		return true;
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