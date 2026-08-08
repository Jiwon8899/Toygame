# ToyGame 빌드 플레이 검증 보고서 — 2026-08-09

## 최종 결과

- Unity 전체 EditMode 테스트: **106/106 성공**, 실패 0 (22.089초)
- Unity 콘솔: **컴파일/빌드 오류 0건**
- WebGL 클린 빌드: **성공**
  - 경로: `Build/WebGL`
  - 크기: 1,137,886,935 bytes (1,085.17 MiB)
  - 빌드 시간: 937.47초
  - 빌드 보고서: 오류 0, 경고 98
- 새 WebGL 브라우저 플레이:
  - 타이틀 → 혼자 시작 → 저장 이어하기 → 게임플레이 진입 성공
  - 첫 번째 뽑기 기계 40회 + 인접 기계 40회, 총 **80회 연속 좌클릭**
  - 이후 15초 물리 방치
  - JavaScript error 0, 오류 대화상자 0, `memory access out of bounds` 재발 0

## 항목별 확인

| 파트 | 결과 | 검증 내용 |
|---|---|---|
| B. WebGL 뽑기 기계 크래시 | 완료 | 유효 스폰 지점 필터, 재사용 버퍼, 유한 좌표 검사, 안전 복귀 적용. 최종 빌드에서 2대 총 80회 공격 + 15초 방치 오류 0. 이전 중간 빌드 포함 누적 220회 좌클릭에서 재발 0. |
| D. 손님 구매/충돌/손 상품 | 완료 | 상품 선택 즉시 진열 수량 감소, 취소 시 복구, 결제 이중 차감 방지. 실제 Play에서 3명의 손 상품 모델 확인, 8명 동시 이동 15초 동안 월드 관통 0건. |
| C. 라이벌 상점 진열 | 완료 | 실제 렌더러 bounds 기반 선반 앵커 및 모델 정규화 적용. 8개 선반 모두 정상 좌표/크기 확인. 방문 revision 1→2에서 상품 ID 전부 변경 확인. |
| F. 직원 기계 배정/자동 운영 | 완료 | 계산대/집게/쿠지 배정 UI, 기계별 안정 ID, 50% 직원 비용, 자동 반복, 저장 복원 적용. 실제 Play에서 쿠지 재고/시도/자금 변화와 직원 도착 확인. 꼬치 R 프롬프트 및 URP Lit 지원 확인. |
| E. 쿠지 긁기 UI | 완료 | 전체 화면 갈색 배경 제거, 카드 중심 투명 오버레이, 안내/진행도/보상 영역 재배치. 실제 Play에서 긁기 화면 겹침 없음 확인. |
| A. 타이틀 문구 | 완료 | `혼자서 자동화로 운영하는 고양이 굿즈 뽑기 소품샵`으로 교체. 에디터와 최종 WebGL 타이틀에서 확인. |

## 원인과 수정 요약

WebGL 크래시는 집게 기계의 상품 복귀 과정에서 파괴/비활성/비정상 좌표 스폰 지점을 반복 순회하고, FixedUpdate 중 후보 목록을 변경·할당하는 경로가 겹친 것이 원인이었다. 반환 대상과 스폰 지점을 스냅샷/재사용 버퍼로 분리하고, 모든 좌표에 유한값 검사를 추가해 IL2CPP/WebAssembly에서 잘못된 메모리 접근으로 이어지는 경로를 차단했다.

## 검증 스크린샷

- `Assets/PickAndPlaceShop/Docs/Verification/title_solo_automation_subtitle.png`
- `Assets/PickAndPlaceShop/Docs/Verification/customer_hand_product_close.png`
- `Assets/PickAndPlaceShop/Docs/Verification/rival_shelves_fixed.png`
- `Assets/PickAndPlaceShop/Docs/Verification/staff_machine_assignment_ui.png`
- `Assets/PickAndPlaceShop/Docs/Verification/staff_operating_kuji_1.png`
- `Assets/PickAndPlaceShop/Docs/Verification/claw_r_prompt_skewer.png`
- `Assets/PickAndPlaceShop/Docs/Verification/kuji_scratch_active.png`

## 참고

- 프로젝트에는 이번 작업 전부터 다수의 미커밋 변경이 존재했다. 사용자 변경을 섞거나 덮어쓰지 않기 위해 WebGL 크래시 수정은 독립 커밋(`8ab5e4f`)으로 보존했고, 기존 변경과 겹치는 나머지 파일은 현재 작업 트리에 저장했다.
- 빌드 경고 98건은 빌드를 막지 않았으며 최종 빌드 결과는 성공이다. 오류는 0건이다.
