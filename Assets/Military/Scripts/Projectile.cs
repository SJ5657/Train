using UnityEngine;

// 포탄/로켓. 거인 몸에 닿으면 피해, 바닥에 닿거나 수명이 끝나면 폭발.
public class Projectile : MonoBehaviour
{
    Vector3 vel;
    float damage, homing, dieAt, explodeSize;
    TrailRenderer trail;
    Vector3 homingOffset;
    static int buildingMask = -1;
    static int BuildingMask => buildingMask >= 0 ? buildingMask : (buildingMask = LayerMask.GetMask("Building"));

    public void Launch(Vector3 velocity, float dmg, float homingRate, float size, TrailRenderer tr)
    {
        vel = velocity; damage = dmg; homing = homingRate; explodeSize = size; trail = tr;
        dieAt = Time.time + 7f;
        homingOffset = Random.insideUnitSphere * 10f; // 유도 목표를 몸통에서 이만큼 어긋나게 → 가끔 빗나감
    }

    void Update()
    {
        if (EnemyFreeze.Active)
        {
            // 보급상자 효과: 날아가던 포탄도 정지 (수명도 같이 멈춤). 멈춘 포탄에 거인이 닿으면 피해를 입고 터짐
            dieAt += Time.deltaTime;
            var fg = GiantHealth.Instance;
            if (fg && !fg.IsDead && fg.IsTouching(transform.position, 1.5f)) { fg.TakeDamage(damage); Explode(transform.position); }
            return;
        }
        float dt = Time.deltaTime;
        var g = GiantHealth.Instance;
        if (homing > 0 && g && !g.IsDead)
        {
            Vector3 target = g.Center + Vector3.up * g.Scale * 0.25f + homingOffset;
            Vector3 desired = (target - transform.position).normalized * vel.magnitude;
            vel = Vector3.RotateTowards(vel, desired, homing * dt, 0f);
        }
        Vector3 next = transform.position + vel * dt;

        if (g && !g.IsDead && (g.IsInsideBody(next, 1.5f) || g.IsInsideBody((transform.position + next) * 0.5f, 1.5f)))
        {
            g.TakeDamage(damage);
            Explode(next);
            return;
        }
        // 건물을 뚫고 지나가지 않음: 건물에 맞으면 그 자리에서 폭발
        if (Physics.Linecast(transform.position, next, out RaycastHit hit, BuildingMask, QueryTriggerInteraction.Ignore)) { Explode(hit.point); return; }
        if (next.y <= 0.3f) { Explode(new Vector3(next.x, 0.5f, next.z)); return; }

        transform.position = next;
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        if (Time.time > dieAt) Explode(transform.position);
    }

    void Explode(Vector3 p)
    {
        CombatFX.Explosion(p, explodeSize);
        if (trail)
        {
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            trail.autodestruct = true;
        }
        Destroy(gameObject);
    }
}
