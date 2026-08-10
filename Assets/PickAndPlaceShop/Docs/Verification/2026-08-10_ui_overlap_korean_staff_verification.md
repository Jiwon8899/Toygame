# UI 겹침·한글 잘림·안내 표시 2차 배치 검증

검증일: 2026-08-10  
대상: ToyGame Unity Editor / WebGL  
최종 판정: **6개 파트 모두 수정 및 검증 완료**

## 결과 요약

| 파트 | 결과 | 핵심 변경 |
|---|---|---|
| A. 가게 이름 입력 중 HUD 겹침 | Fixed | 이름 입력 모달 동안 게임플레이 HUD를 단일 억제 경로로 숨기고 완료 후 복구 |
| B. 뽑기 중 평판 UI 겹침 | Fixed | 로컬 뽑기 조작 중 평판 칩만 숨기고 종료 즉시 복구 |
| C. 튜토리얼 문장 잘림 | Fixed | TMP/UGUI 텍스트 높이와 말풍선 높이를 내용에 맞춰 자동 확장 |
| D. 상단 뉴스 두 번째 줄 잘림 | Fixed | 원문은 정상임을 확인하고 표시 영역을 112~240px로 자동 확장 |
| E. 마감 정산 목적지 불명확 | Fixed | 튜토리얼 7/7 동안 마감 위치 위에 빨간 `!` 월드 표식 표시 |
| F. 알바 배치 진입 동선 | Fixed | 독립 상단 버튼을 제거하고 업그레이드 창 상단에 배치 버튼 통합 |

## PART A — 이름 입력 모달

- 기존 구현은 HUD 억제 자체는 있었으나, 모달 Canvas 정렬값 `50000`이 Unity의 signed 16-bit 정렬 범위를 넘어 `-15536`으로 래핑되어 다른 UI 뒤로 내려가는 추가 원인이 있었다.
- 정렬값을 안전한 최상위 값 `32760`으로 수정했다.
- `ShopInputModeManager`에 게임플레이 HUD 억제 소유자를 두고, 이름 입력 시작·확정·복원·파괴 경로가 같은 API를 사용하도록 정리했다.
- 이름 입력 중 `ShowsGameplayHud=false`, 확정 후 `true`로 복구됨을 확인했다.
- 1366×768과 1920×1080에서 제목·입력란은 온전히 보이고 상단 배너와 하단 퀵슬롯은 숨겨졌다.

증거: [1366×768](UIBatch2/A_naming_1366x768.png), [1920×1080](UIBatch2/A_naming_1920x1080.png), [확정 후 HUD 복구](UIBatch2/A_after_confirm_1366x768.png)

## PART B — 뽑기 중 평판 UI

- 정보 손실을 최소화하기 위해 전체 상태 HUD를 없애지 않고, 뽑기 조작 화면과 직접 겹치던 **평판 칩만** `LocalOperatorActive` 동안 숨기는 방식을 선택했다.
- 조작 중 `reputation=false`, `localOperator=true`; 종료 후 `reputation=true`, `localOperator=false`를 확인했다.
- 일차와 자금 정보는 유지되며 평판은 뽑기 종료 즉시 복구된다.

증거: [뽑기 조작 중 평판 숨김](UIBatch2/B_claw_reputation_hidden_1366x768.png)

## PART C — 튜토리얼 말풍선

- 원인: 고정 높이 텍스트 박스와 고정 패널 높이로 인해 긴 문장이 `Truncate`와 유사하게 잘렸다.
- 처리: 세로 Overflow를 허용하고 `preferredHeight` 기준으로 텍스트와 패널을 자동 확장했다. 패널 높이는 튜토리얼 기준 140~240px이다.
- 1/7~7/7 전체 문장을 코드로 순회해 확인했다. 측정 결과는 순서대로 `50/140`, `50/140`, `75/153`, `75/153`, `50/140`, `75/153`, `50/140`(선호 텍스트 높이/패널 높이)이며 모든 원문이 유지됐다.
- 6/7은 1366×768에서도 원문 전체가 표시됐다.

증거: [1366×768](UIBatch2/C_tutorial_6of7_1366x768.png), [1920×1080](UIBatch2/C_tutorial_6of7_1920x1080.png), [WebGL 1366×768](UIBatch2/WebGL_C_D_1366x768.png)

## PART D — 상단 뉴스 배너

- 원문 데이터는 완전했다. 문제는 두 줄을 담지 못하는 고정 표시 영역이었다.
- 문장을 줄이지 않고 세로 Overflow와 `preferredHeight + 32px` 기반 112~240px 자동 높이를 적용했다.
- 다음을 포함한 5종 대표 뉴스 문자열을 순환 검증했다.
  1. 포근한 고양이 인형 인증 사진 소식
  2. 새로운 고양이 인형 수집 영상 소식
  3. 동네 축제 고양이 인형 전시 소식
  4. 정교한 고양이 피규어 사진 소식
  5. 한정 고양이 피규어 개봉 영상 소식
- 측정 패널/선호 높이는 `150/118`, `112/79`, `112/79`, `112/79`, `150/118`이었고 두 번째 줄 끝까지 표시됐다.

증거: [1366×768](UIBatch2/D_long_news_1366x768.png), [1920×1080](UIBatch2/D_long_news_1920x1080.png), [WebGL 1366×768](UIBatch2/WebGL_C_D_1366x768.png)

## PART E — 마감 목적지 표식

- 기존 별도 목적지 표식 시스템은 없어 튜토리얼 HUD 내부의 수명 관리와 월드 빌보드 유틸리티를 재사용했다.
- 튜토리얼 7/7에서 활성 마감 상호작용 지점 `(11.50, 0.00, -4.40)` 위 2.4m에 빨간 `!`를 표시한다.
- 표식은 항상 카메라를 바라보며, 튜토리얼 완료 상태에서는 제거됨을 확인했다.
- 다른 단계에서 추가로 목적지가 불명확한 항목은 이번 검증에서 발견되지 않았다.

증거: [마감 위치 표식](UIBatch2/E_closing_marker_1366x768.png)

## PART F — 알바 배치 버튼

- 기존 상단 독립 `알바 고용/배치 관리` 버튼을 제거하고 업그레이드 창 상단 자금 표시 옆에 `알바 배치` 버튼을 배치했다.
- 기존 단일 진입 함수 `OpenStaffManagement`를 그대로 호출한다.
- 고용된 알바가 없을 때 버튼은 비활성이고 `고용된 알바가 없습니다`를 표시한다.
- 알바가 있을 때 활성화되며, 2번 알바를 뽑기 #101에 지정한 뒤 저장 값이 `1101`로 즉시 바뀌고 NPC 위치가 실제로 이동하는 것을 확인했다.

증거: [미고용 상태](UIBatch2/F_upgrade_no_staff_1920x1080.png), [1366×768 배치 버튼](UIBatch2/F_upgrade_1366x768.png), [알바 배치 창](UIBatch2/F_staff_assignment_1920x1080.png)

## 회귀 및 빌드 검증

- Unity Editor Play Mode: 6개 파트 실제 동작 확인.
- 해상도: 1366×768, 1920×1080 모두 확인.
- Unity Console: 컴파일 및 검증 종료 시 error 0건.
- WebGL 빌드: 성공, `Build/WebGL_UIBatch2`, 총 344.41MB, build error 0건.
- WebGL 브라우저 실플레이: 신규 게임 → 이름 입력 → 게임플레이 진입 성공.
- WebGL에서 C/D 글꼴 렌더링과 전체 문장 표시를 확인했다.
- 브라우저 콘솔에 C# 예외, NullReferenceException, 크래시는 없었다. 비치명적인 기존 셰이더 플랫폼/URP FSR 경고와 오디오 메타데이터 준비 로그만 확인됐다.
- 상단 HUD 일반 표시, 튜토리얼 진행, 뽑기 진입·종료, 알바 배치 기본 흐름에 회귀 문제 없음.

## 관련 커밋

- `369c8be` fix(ui): suppress HUD during naming
- `1fd47f3` fix(ui): move staff placement into upgrades
- `9199870` fix(ui): grow tutorial objective bubble
- `651b956` fix(ui): expand daily news banner
- `cdf1e41` fix(ui): hide reputation during claw play
- `d8f6295` feat(tutorial): mark closing destination
- `63889a3` fix(ui): correct naming modal sort order

## 가정

- 뽑기 화면에서는 정보 손실을 줄이기 위해 겹치는 평판 칩만 숨기고 일차·자금 칩은 유지했다.
- 마감 표식은 기존 UI 톤을 해치지 않도록 사운드나 화면 흔들림 없이 빨간 `!`만 사용했다.
- 긴 튜토리얼·뉴스 문장은 축약하지 않고 패널을 확장하는 것을 우선했다.
