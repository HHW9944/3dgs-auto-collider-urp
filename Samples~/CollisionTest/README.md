# Collision Test

이 폴더에는 PLY나 기존 프로젝트의 Scene이 들어 있지 않습니다. 특정 컴퓨터의 경로에 연결하지 않기 위해서입니다.

새 프로젝트에서 메뉴 `Gaussian Tool/Create Collision Test Objects`를 실행하면 현재 Scene에 다음이 생깁니다.

- `CollisionTestGround`: 정적 BoxCollider가 있는 바닥
- `CollisionTestSphere`: Rigidbody가 있는 구체. Play를 누르면 아래로 떨어집니다.

Gaussian Collider와의 충돌은 바닥 프리미티브가 아니라, 같은 Scene에 만든 `GaussianPhysicsObject`의 생성 Collider로 확인합니다. 사용 순서는 `Documentation~/README.md`를 따릅니다.
