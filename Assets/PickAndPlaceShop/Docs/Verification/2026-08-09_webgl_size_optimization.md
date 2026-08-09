# WebGL 빌드 용량 감축 보고서 (2026-08-09)

## 진행 상태

- 기준 커밋: `d3897b9b103081a0fe43cf38464960c712ae324a`
- Unity: 6000.4.6f1 / WebGL
- 작업 전 `docs`: 792.25 MiB
- 작업 전 압축 전 사용자 에셋: 1.6 GiB
- PART A 진단: 완료

## PART A-1. 작업 전 Build Report

최신 완성 빌드의 `Editor-prev.log` 내 Build Report를 기준으로 했다.

| 종류 | 압축 전 용량 | 비중 |
|---|---:|---:|
| Textures | 801.1 MiB | 48.5% |
| Meshes | 822.7 MiB | 49.8% |
| Animations | 1.9 MiB | 0.1% |
| Sounds | 1.5 MiB | 0.1% |
| Shaders | 4.0 MiB | 0.2% |
| Other Assets | 18.9 MiB | 1.1% |
| Levels | 0 KiB | 0.0% |
| File headers | 354.3 KiB | 0.0% |
| 합계 | 1.6 GiB | 100% |

### 용량 상위 50개 파일

| 순위 | 압축 전 용량 | 비중 | 경로 |
|---:|---:|---:|---|
| 1 | 21.3 MiB | 2.5% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_SpawnPoint_MetallicSmoothness.png` |
| 2 | 21.3 MiB | 2.5% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_SpawnPoint_Normal.png` |
| 3 | 15.7 MiB | 1.8% | `Assets/PickAndPlaceShop/Art/Fonts/NotoSansKR-Regular.otf` |
| 4 | 10.8 MiB | 1.3% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_035_00.asset` |
| 5 | 10.8 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_074_00.asset` |
| 6 | 10.7 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_061_00.asset` |
| 7 | 10.7 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_049_00.asset` |
| 8 | 10.7 MiB | 1.2% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_SpawnPoint_Emissive.png` |
| 9 | 10.7 MiB | 1.2% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_SpawnPoint_BaseColor.png` |
| 10 | 10.7 MiB | 1.2% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_Glow_Emissive.png` |
| 11 | 10.7 MiB | 1.2% | `Assets/Shooter/Art/Environment/SpawnPad/Tex_Glow_Opacity.png` |
| 12 | 10.7 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_056_00.asset` |
| 13 | 10.6 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_038_00.asset` |
| 14 | 10.6 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_073_00.asset` |
| 15 | 10.5 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_055_00.asset` |
| 16 | 10.5 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_047_00.asset` |
| 17 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_042_00.asset` |
| 18 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_052_00.asset` |
| 19 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_048_00.asset` |
| 20 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_007_00.asset` |
| 21 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_057_00.asset` |
| 22 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_025_00.asset` |
| 23 | 10.4 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_050_00.asset` |
| 24 | 10.3 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_030_00.asset` |
| 25 | 10.3 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_004_00.asset` |
| 26 | 10.3 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_037_00.asset` |
| 27 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_054_00.asset` |
| 28 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_069_00.asset` |
| 29 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_058_00.asset` |
| 30 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_053_00.asset` |
| 31 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_023_00.asset` |
| 32 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_065_00.asset` |
| 33 | 10.2 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_005_00.asset` |
| 34 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_051_00.asset` |
| 35 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_043_00.asset` |
| 36 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_010_00.asset` |
| 37 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_064_00.asset` |
| 38 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_059_00.asset` |
| 39 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_017_00.asset` |
| 40 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_006_00.asset` |
| 41 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_070_00.asset` |
| 42 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_019_00.asset` |
| 43 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_003_00.asset` |
| 44 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_011_00.asset` |
| 45 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_022_00.asset` |
| 46 | 10.1 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_014_00.asset` |
| 47 | 10.0 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_078_00.asset` |
| 48 | 10.0 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_036_00.asset` |
| 49 | 10.0 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_075_00.asset` |
| 50 | 10.0 MiB | 1.2% | `Assets/PickAndPlaceShop/Resources/ProductVisuals/Generated/Meshes/ProductMesh_013_00.asset` |

## PART A-2. Shooter 조사

- 디스크 용량: 22.84 MiB / 194개 파일
- 폰트 `GFCRedSpirit-Medium.otf`는 Shooter가 아니라 `Assets/Core/Resources/Fonts/`에 있으며 유지 대상이다.
- SpawnPad 프리팹은 현재 게임 씬들에서 참조되고 있어 폴더 전체를 미사용으로 삭제할 수 없다.
- SpawnPad의 고해상도 텍스처 6개가 빌드에서 압축 전 85.4 MiB를 차지한다.
- 원인: `Pfb_SpawnPad`가 메인 게임 씬을 포함한 여러 씬에 배치되어 머티리얼과 텍스처가 빌드 의존성으로 들어온다.

## PART A-3. Assets 최상위 폴더

| 폴더 | 디스크 용량 | 판정 |
|---|---:|---|
| PickAndPlaceShop | 2051.35 MiB | 핵심 게임 코드/씬/생성 상품. 생성 메시가 과도함 |
| reduced | 628.04 MiB | 상품 80종 원본 GLB. 생성 래퍼 80개가 다시 참조함 |
| 외형들모음 | 614.10 MiB | 플레이어/NPC/집게 외형으로 사용 |
| Core | 72.08 MiB | 공용 런타임, TMP, 폰트로 사용 |
| animation | 54.47 MiB | 플레이어/NPC 애니메이션으로 사용 |
| Screenshots | 38.73 MiB | 검증 자료. 빌드에는 포함되지 않음 |
| Shooter | 22.84 MiB | SpawnPad만 게임 씬에서 사용, 나머지는 후보 |
| _Recovery | 4.03 MiB | Unity 복구 씬. 사용자 작업으로 보존 |
| Low-Poly_Objects_Pack | 3.62 MiB | 기존 상품/소품 참조 후보, 임의 삭제 금지 |

## PART A-4. 원인 결론

- 최신 Build Report의 상위 폴더 집계: PickAndPlaceShop 903 MiB, reduced 480 MiB, Shooter 85.4 MiB, Core 31.8 MiB, NPC 외형 6.1 MiB.
- 상품 래퍼 80개: 12,260,614 정점 / 21,104,359 삼각형 / 메시 에셋 1,605.80 MiB.
- 80개 래퍼 모두 `Assets/reduced/*.glb`를 추가 의존하므로, 생성 메시와 원본 GLB가 중복 포함된다.
- 따라서 792.25 MiB 배포물의 본체는 상품 외형 중복 및 지나치게 높은 폴리곤 수이고, 다음 우선순위는 SpawnPad 텍스처다. 오디오는 1.5 MiB라 주원인이 아니다.

## 후속 파트 기록

PART B~G 결과는 각 단계 검증 후 이 문서에 이어서 기록한다.

## PART B. Resources 감사

| Resources 경로 | 작업 전 | 작업 후 | 처리 |
|---|---:|---:|---|
| `Assets/PickAndPlaceShop/Resources` | 1,613.75 MiB / 1,579파일 | 0.37 MiB / 569파일 | 생성 외형·아이콘·머티리얼을 GUID 보존 이동 |
| `Assets/Core/TextMesh Pro/Resources` | 2.17 MiB | 2.17 MiB | TMP 필수 리소스 유지 |
| `Assets/Core/Resources` | 0.54 MiB | 0.54 MiB | GFCRedSpirit 및 전역 폰트 설정 유지 |
| `Assets/Resources` | 0 | 0 | 빈 폴더 |

- 직접 `Resources.Load`/`LoadAll`로 읽는 상품 정의, 진행·오디오·월드 설정, TMP/전역 폰트와 `RuntimeLitBase`는 유지했다.
- 상품 외형과 아이콘은 `ShopProductDefinition`의 직렬화 참조로 접근하므로 `Assets/PickAndPlaceShop/Generated`로 이동했다.
- `FindIcon`은 먼저 상품 정의의 아이콘 참조를 사용하고, 구형 데이터만 기존 Resources 폴백을 사용하도록 변경했다.
- 이동은 Unity `AssetDatabase.MoveAsset`만 사용해 GUID를 보존했다.
- 이 단계는 Resources 강제 포함을 제거하지만 모든 200개 상품 정의가 80개 외형을 참조하므로, 실제 빌드 감축의 본체는 다음 단계의 중복 메시 제거와 원본 GLB 최적화다.
