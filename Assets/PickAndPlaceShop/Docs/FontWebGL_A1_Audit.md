# GFCRedSpirit WebGL A-1 실태 감사

검사 일시: 2026-08-09  
대상 씬: `Assets/PickAndPlaceShop/Scenes/PickAndPlaceShop_MainStreetSlice_Multiplayer.unity`

## A-1-1 폰트 자산

| 항목 | 실측 결과 |
| --- | --- |
| 원본 폰트 | `Assets/Shooter/GFCRedSpirit-Medium.otf` 존재 |
| 원본 GUID | `2ef0b417c3f6029429bc266024c9543d` |
| Font Data 포함 | `includeFontData: 1` |
| TMP Font Asset | `Assets/Shooter/GFCRedSpirit-Medium SDF.asset` 존재 |
| TMP GUID | `768e8c35165f977449ad074302184a0f` |
| Atlas Population Mode | Dynamic |
| Atlas | 1024 x 1024 |
| Multi Atlas | Enabled |
| Source Font File | 정상, 위 OTF 직접 참조 |
| EditMode 문자/글리프 수 | 0 / 0 (Dynamic 런타임 생성 방식) |
| Render Mode | SDFAA 계열 |

TMP Font Asset 자체는 요구 조건을 이미 충족한다. 재생성보다 GUID와 참조를 보존한 이동·설정 정비가 안전하다.

## A-1-2 할당 경로

- 씬과 프리팹의 여러 `Text` / `TextMesh` 컴포넌트가 OTF를 직접 참조한다.
- `Assets/Core/Resources/GlobalGameFontSettings.asset`은 TMP 자산을 직접 참조한다.
- `Assets/Core/Scripts/Runtime/Framework/UI/GlobalGameFontApplier.cs`가 `BeforeSceneLoad`에서 시작해 약 0.25초마다 로드된 모든 `Text`, `TextMesh`, `TMP_Text`, UI Toolkit 텍스트에 전역 폰트를 적용한다.
- 감사 시점의 `GlobalGameFontSettings.asset`은 TMP만 GFCRedSpirit이고, Legacy Regular/Medium/Bold는 Noto Sans KR이었다.
- `Assets/PickAndPlaceShop/Resources/ShopUiTheme.asset` 역시 런타임 생성 UI용 Legacy 폰트를 Noto Sans KR로 제공한다.
- 프로젝트 Resources 경로에 `TMP Settings.asset`이 없으며 `TMP_Settings.instance`도 null이었다.

## A-1-3 적용 대상 목록

현재 메인 게임 씬 실측:

- Canvas `UnityEngine.UI.Text`: 81개
- World `UnityEngine.TextMesh`: 38개
- `TMP_Text`: 0개

프로젝트 자체 프리팹(패키지 디버그 프리팹 제외) 대표 대상:

- `ClawMachine_101` ~ `ClawMachine_105`: 각 Canvas Text 5개, World TextMesh 2개
- `ShopCustomer_Network`: World TextMesh 1개

월드 텍스트 그룹:

- `ShopSign`, `ShopSign (1)`, `ArcadeSign`, `오늘의 인기 뽑기`
- `가챠샵_간판`, `쿠지샵_간판`, `가챠 · 쿠지 전문점`
- `BoardLabel`, `ShelfLabel`, `RegisterLabel`, `PackingStationLabel`
- `MachineLabel`, `PriceDisplay`, `RenewalWallSign`, `손님 리뷰 게시판_Label`
- `UpgradeTitle`, `UpgradeSummary`, `진행 안내`, `쿠지 재고판`

Canvas / 동적 UI 그룹:

- HUD: 일차·시간·자금·평판·목표·상태·상호작용 안내
- 진열/상점/업그레이드/고용 패널과 버튼 라벨
- 인벤토리·창고·도감·퀵슬롯·상품명·금액
- 일일 마감·튜토리얼·알림 토스트·확인 대화상자
- 설정·일시정지·게임 방법 화면

현재 프로젝트는 지시서의 예상과 달리 화면 텍스트 대부분이 TMP가 아니라 Legacy `Text` / `TextMesh`다. 따라서 실제 표시 계층을 GFCRedSpirit로 통일하면서, 새 TMP 기본값도 같은 자산으로 고정해야 양쪽이 모두 해결된다.

## A-1-4 WebGL 대체 폰트 특정

빌드에서 보인 대체 폰트는 LiberationSans가 아니라 전역 런타임 설정이 강제로 지정한 Noto Sans KR 계열이다. 근거:

1. Scene/Prefab의 GFC 직접 참조는 에디터 Scene View에서 정상 표시된다.
2. 플레이 시작 후 `GlobalGameFontApplier`가 Legacy `Text` / `TextMesh`를 `GlobalGameFontSettings`의 Noto Sans KR로 덮어쓴다.
3. 한글이 깨지지 않고 다른 모양으로만 보인 증상과 일치한다.
4. TMP Settings 부재는 TMP 기본값·fallback이 빌드에서 불안정해지는 별도 구조 결함이다.

## 수정 전 결론

근본 원인은 OTF 글리프 누락이나 TMP atlas 설정 실패가 아니라, 런타임 전역 폰트 설정이 Scene의 GFCRedSpirit 직접 참조를 Noto Sans KR로 덮어쓰는 설정 불일치다. 동시에 TMP Settings가 Resources에 없어 향후 TMP 동적 UI의 빌드 기본값도 보장되지 않는다.

다음 단계는 GUID를 유지한 Unity `AssetDatabase.MoveAsset` 이동, Legacy/TMP 전역 설정의 GFCRedSpirit 단일화, Resources 안 TMP Settings 생성, WebGL 재빌드·브라우저 검증이다.
