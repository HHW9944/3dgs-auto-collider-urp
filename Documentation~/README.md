# 3DGS Physics Toolkit

Unity 2022.3에서 PLY Gaussian 데이터로 BoxCollider를 만드는 로컬 패키지입니다. 화면의 Gaussian 렌더링은 이 패키지에 포함하지 않습니다. [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) `1.1.2`, 커밋 `ebd76138a09b68340db3c74b3867f0451a53b6cd`를 새 프로젝트에 직접 설치합니다.

이 패키지를 현재 개발 프로젝트의 `Packages`에 넣지 마세요. `Assets/Scripts`에 같은 클래스가 남아 있으면 타입이 중복되고 `.ply` 임포터도 두 개가 됩니다.

## 1. 툴킷 설치

Unity 2022.3.47f1, Built-in Render Pipeline 프로젝트를 만듭니다. 패키지 폴더 `com.3dgs.physics-toolkit`를 새 프로젝트 밖에 둔 뒤, 새 프로젝트의 `Packages/manifest.json`에 다음을 추가합니다.

```json
"com.3dgs.physics-toolkit": "file:C:/path/to/com.3dgs.physics-toolkit"
```

경로는 그 컴퓨터의 패키지 폴더이며, 슬래시는 `/`를 사용합니다. PLY 경로나 개발 프로젝트의 `Assets` 경로는 쓰지 않습니다. Unity 2022.3에서는 `file:../...`처럼 프로젝트 폴더 밖으로 나가는 상대 경로가 거부될 수 있으므로 절대 경로를 씁니다.

폴더를 새 프로젝트의 `Packages/com.3dgs.physics-toolkit`로 복사해도 됩니다. 복사한 경우에는 manifest 항목이 자동으로 생깁니다.

## 2. 외부 렌더러 설치

같은 `Packages/manifest.json`에 렌더러를 추가합니다. 이 패키지의 `package.json`에는 Git 주소를 넣지 않습니다. Unity Package Manager는 패키지 선언 파일의 Git 의존성을 설치하지 않습니다.

```json
"org.nesnausk.gaussian-splatting": "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package#ebd76138a09b68340db3c74b3867f0451a53b6cd"
```

설치가 끝나면 Package Manager가 다음을 함께 받습니다.

- `com.unity.burst` 1.8.8 이상
- `com.unity.collections` 2.1.4
- `com.unity.mathematics` 1.2.6

렌더러를 빼면 `GaussianSplatAsset`과 `GaussianSplatRenderer`를 참조하는 코드가 컴파일되지 않습니다. 셰이더 GUID는 위 커밋의 패키지 메타에 있습니다. 다른 커밋을 설치하면 렌더러 셰이더 자동 연결이 실패할 수 있습니다.

## 3. PLY에서 SplatData 만들기

`.ply` 파일을 새 프로젝트의 `Assets` 안으로 복사합니다. 툴킷의 `SplatImporter`가 그 파일을 `SplatData`로 가져옵니다. 좌표는 기존과 같이 `(-x, -y, -z)`입니다.

이 임포터는 프로젝트의 모든 `.ply`에 적용됩니다. 메시용 PLY와 같이 쓰지 마세요.

## 4. GaussianSplatAsset 만들기

메뉴 `Tools/Gaussian Splats/Create GaussianSplatAsset`에서 같은 PLY 파일을 선택합니다. 이 메뉴는 외부 렌더러 패키지의 기능입니다. 만들어지는 에셋의 좌표는 파일 좌표 그대로입니다.

## 5. Gaussian Physics Object 만들기

메뉴 `Gaussian Tool/Collider Generator`를 엽니다.

1. Splat Data에 방금 만든 `SplatData`를 넣습니다.
2. `Aras Renderer`를 켭니다.
3. Gaussian Splat Asset에 4번 에셋을 넣습니다.
4. Collider 모드를 고릅니다. 기본값은 Legacy입니다.
5. 생성 버튼을 누릅니다.

Hierarchy는 기존과 같습니다.

- `GaussianPhysicsObject_<name>`
- `GaussianVisual/GaussianSplatRenderer`, Local Scale `(-1, -1, -1)`
- `GaussianPhysics/GeneratedColliders`

## 6. Collider 생성

Inspector의 `Hierarchy 구성 및 Collider 생성`으로 다시 만들 수 있습니다.

- Legacy: 밀도 임계값을 넘긴 복셀의 축 정렬 BoxCollider
- Surface: 표면 후보에 PCA Oriented BoxCollider. 실패 칸은 이웃 평면을 시도하고, 그것도 실패하면 기존 정육면체 fallback

Surface는 Auto Level을 적용하지 않습니다. 루트에 회전이 남아 있으면 Reset Leveling 후 다시 생성합니다.

## 7. Scene에서 확인

`GaussianPhysicsObject`를 선택하면 Gizmo가 실제 BoxCollider의 Transform, center, size를 그립니다.

- 하늘색: PCA 성공
- 초록색: 이웃 평면으로 생성
- 주황색: Legacy fallback

Console의 `[SurfaceCollider]` 첫 줄에서 표면 후보, PCA 성공, 이웃 성공, fallback, 전체 Collider 수, 시간을 확인합니다.

## 8. 낙하 충돌 테스트

메뉴 `Gaussian Tool/Create Collision Test Objects`는 현재 Scene에 정적 바닥과 Rigidbody 구체를 만듭니다. Gaussian 충돌을 보려면 구체를 생성한 Collider 위에서 Play합니다. 바닥 프리미티브와 Gaussian 오브젝트가 겹치면 구체가 바닥 박스에 먼저 닿을 수 있으니, 테스트 중에는 바닥을 비활성화합니다.

## 직접 선언하는 의존성과 수동 설치

| 항목 | 처리 |
| --- | --- |
| Unity 2022.3, Built-in RP, Physics 모듈 | 프로젝트 자체 |
| `org.nesnausk.gaussian-splatting` 1.1.2, 커밋 `ebd76138` | 새 프로젝트 manifest에 수동 추가 |
| Burst, Collections, Mathematics | 렌더러 설치 시 자동 |
| 이 툴킷 | manifest의 `file:` 또는 `Packages` 복사 |

커스텀 `GaussianSplat` 셰이더는 Built-in RP용입니다. URP/HDRP에서는 그 경로의 재질이 분홍색이 될 수 있습니다. `GaussianSplatRenderer` 경로는 외부 패키지가 파이프라인을 처리합니다.

성능 로그는 호스트 프로젝트의 `Assets/GaussianPhysicsStats.csv`에 추가됩니다.
