# 날짜 전환 상태 보존·UI 입력 복구 검증 보고서

검증일: 2026-08-10  
대상: `ToyGame` / WebGL Development Build

## 1. 최종 상태

- 날짜 전환 후 영구 상태 보존: 완료
- 일일 상태만 초기화: 완료
- Y 두 번 준비 구간 건너뛰기: 완료
- UI 중첩 소유권 및 마지막 UI 종료 후 입력 복구: 완료
- WebGL 이어하기와 포인터 잠금 복구: 완료
- WebGL 빌드: 성공, 오류 0건

## 2. PART A-1 재현 스택

수정 전 MCP 추적 스택은 다음과 같았다.

```text
at System.Environment.get_StackTrace()
at MCPDynamicCode.<Execute>m__0(Int32 previous, Int32 current)
at Unity.Netcode.NetworkVariable`1[T].set_Value(T value)
at PickAndPlaceShop.ShopNetworkGame.UseClosingBell()
  in Assets/PickAndPlaceShop/Scripts/ShopNetworkGame.cs:1170
at PickAndPlaceShop.ShopNetworkGame.ServerFinishDayFromTimer()
  in Assets/PickAndPlaceShop/Scripts/ShopNetworkGame.cs:2083
```

일반 Unity 스택도 `ServerFinishDayFromTimer -> UseClosingBell -> Coins.Value` 순서로 동일했다.

## 3. PART A-2 원인 가설 검증

| 가설 | 결과 |
|---|---|
| 새 게임 초기화 함수가 날짜 전환에도 호출 | 아님 |
| 네트워크 상태 재생성 | 아님 |
| 씬 재로드 | 아님 |
| 자동 저장 실패 | 아님 |
| 마감 비용 차감과 당일 초기화가 영구 상태를 덮어씀 | 맞음 |

직접 원인은 `UseClosingBell()`이 임대료·급여 차감 후 공동 자금을 `0`까지 클램프하고, `ServerReturnAllDisplayedToStorage()`를 호출해 공용 진열을 비우던 것이었다.

## 4. 상태 초기화 기준

| 상태 | 날짜 전환 시 처리 |
|---|---|
| 공동 자금 | 유지(일일 비용 차감 시 최소 1원 보존) |
| 개인 인벤토리 | 유지 |
| 공용 창고 | 유지 |
| 공용 진열 | 유지 |
| 업그레이드·확장 | 유지 |
| 알바 배치 | 유지 |
| 컬렉션 | 유지 |
| 오늘 매출·판매·방문·쓰레기 카운터 | 0으로 초기화 |
| 오늘의 유행·목표·일일 원장 | 다음 날 값으로 재생성 |

## 5. PART A 수정 내용

- 신규 캠페인 초기화를 `InitializeNewCampaignState`로 분리했다.
- 마감 처리에서 공용 진열을 창고로 강제 반환하지 않도록 변경했다.
- `ServerTryPayDailyExpense`로 비용 처리를 단일화하고 영구 자금이 0으로 소실되지 않게 했다.
- `ShopNightSalesSystem`에서 일일 카운터와 임시 집합만 초기화하고 다음 날 판매 원장을 재생성한다.

## 6. PART A 검증 결과

- 1일차에서 자금 20원, 개인/창고/진열 각 2개, 업그레이드·알바·확장·컬렉션을 시드했다.
- 2일차, 3일차, 4일차까지 연속 전환 후 위 영구 상태가 동일하게 유지됐다.
- 매일 `sold/trash/visits`는 0으로 초기화됐다.
- Y 두 번 `ServerSkipPreparation()` 후 `Setup -> Open` 전환과 영구 상태 유지가 확인됐다.
- 저장 파일을 백업한 뒤 복원했으며 실제 WebGL 타이틀의 이어하기 경로에서 `CONTINUE_RESTORE_COMPLETE`, `SOLO_STARTED`가 확인됐다.
- 관련 스크린샷: `2026-08-10_rollover_before.png`, `2026-08-10_rollover_after_day2.png`, `2026-08-10_rollover_after_day4.png`, `2026-08-10_webgl_final_continue.jpg`.

## 7. PART B 원인

- 카메라는 절대 마우스 좌표가 아니라 `<Pointer>/delta`를 사용하므로 화면 우측 좌표 제한 자체가 원인은 아니었다.
- WebGL에서 UI 종료 후 `CursorLockMode.None` 상태가 남아 포인터가 브라우저 가장자리에 닿으면 우측 회전량이 더 이상 들어오지 않았다.
- 여러 UI가 각각 커서 상태를 직접 변경하고 마지막 UI 소유자가 누구인지 일관되게 판단하지 못하는 경로도 있었다.

## 8. UI 소유자 전수 목록

아래 UI가 공용 `ShopInputModeManager.Push/Pop` 경로를 사용함을 확인했다.

- 캡슐 개봉 결과
- 인벤토리·창고·공용 진열
- 집게 기계 조작 UI
- 마감 정산
- 쿠지 긁기
- 메인 메뉴
- 흥정
- 일시정지 메뉴
- 진행 HUD 및 튜토리얼 스킵 확인
- 가게 이름 입력
- 업그레이드 UI
- 엔딩 UI

## 9. PART B 수정 내용

- 입력 모드 소유자를 스택으로 관리하고 마지막 UI가 닫힐 때만 게임플레이 입력을 복구한다.
- 커서 잠금·표시와 플레이어 이동·카메라·상호작용 활성화를 `ShopInputModeManager` 한 곳에서 적용한다.
- WebGL은 사용자 입력 제스처에서만 포인터 잠금을 요청한다.
- 이미 잠긴 포인터를 매 프레임 재요청하지 않도록 해 중복 `requestPointerLock`을 제거했다.
- 메뉴·엔딩·게임 매니저의 직접 커서 제어를 제거했다.

## 10. PART B 검증 결과

- PlayMode 중첩 검사: `Gameplay -> UI(1) -> UI(2) -> UI(1) -> Gameplay` 순서에서 마지막 UI가 닫힐 때만 `Locked/hidden`으로 복구됐다.
- WebGL에서 이어하기 후 캔버스가 활성 요소이고 `document.pointerLockElement != null`임을 확인했다.
- 최종 빌드에서 인벤토리 열기와 UI 입력 차단이 정상이며, 닫은 뒤 카메라 우측 회전 전/후 화면 변화가 확인됐다.
- 최종 브라우저 콘솔에는 `NullReferenceException`, `WrongDocumentError`가 기록되지 않았다.
- 관련 스크린샷: `2026-08-10_webgl_final_inventory_open.jpg`, `2026-08-10_webgl_inventory_closed_before_turn.jpg`, `2026-08-10_webgl_inventory_closed_after_right_turn.jpg`.

## 11. 회귀 테스트

- `ShopInputInteractionRegressionTests`: 23/23 통과.
- 날짜 전환, Y 두 번 준비 스킵, 메인 메뉴 입력 모드, WebGL 포인터 잠금 재획득 테스트 포함.
- 전체 EditMode: 112개 중 111개 통과. 실패 1개는 기존 `ShopCatThemeCatalogTests.GeneratedVisuals_AreNormalizedAndPhysicsFree`이며 이번 날짜·입력 변경과 무관하다.
- WebGL 이어하기: 컨테이너 복원 완료와 싱글 시작 로그 확인, NullReferenceException 없음.

## 12. WebGL 빌드 결과

- 경로: `Build/WebGL_DayCursor_20260810`
- 결과: 성공
- 소요 시간: 820.24초
- 크기: 344.43 MB
- 빌드 오류: 0건
- 빌드 경고: 109건
- 브라우저 비치명 로그: 일부 오디오 메타데이터 로딩 지연, WebGL 미지원 VFX 셰이더 경고.

## 13. 보류·가정

- 이번 변경과 무관한 카탈로그 시각 프리팹 검사 1건은 범위 밖이라 수정하지 않았다.
- 일일 비용은 경제 시스템을 유지하되 보유 자금 전액 소실을 막기 위해 최소 1원을 보존하도록 해석했다.
- WebGL 포인터 잠금은 브라우저 보안 정책상 실제 키·마우스 사용자 제스처에서만 재획득하도록 했다.

## 커밋

- `7131597 fix(day): preserve state across rollover`
- `639843b fix(input): restore WebGL pointer lock`
- `37c3bae fix(input): avoid duplicate WebGL lock request`
