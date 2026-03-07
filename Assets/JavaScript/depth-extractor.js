// Assets/JavaScript/depth-extractor.js
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — Composite Depth Score Extraction Engine
// ══════════════════════════════════════════════════════════════════════
//
// CEF에 주입되어 웹 페이지의 DOM/CSS 신호로부터 깊이 맵을 생성한다.
// 6종의 시각적 깊이 신호를 가중 합산하여 512×512 그레이스케일 캔버스에 렌더링.
//
// 아키텍처:
//   SignalRegistry   — 확장 가능한 신호 추출기 레지스트리 (플러그인 패턴)
//   DepthScorer      — 등록된 신호들의 가중 합산으로 깊이 점수 산출
//   DepthRenderer    — DOM 순회 및 깊이 캔버스 렌더링
//   DOMWatcher       — MutationObserver + 이벤트 기반 변경 감지
//   DepthExtractor   — 오케스트레이터 (초기화, 공개 API)
//
// 확장 방법:
//   window.__UIShader.registerSignal('mySignal', 0.1, extractorFn);
//   → 새로운 깊이 신호를 코어 수정 없이 추가 가능

(function () {
    'use strict';

    // 이미 주입되었으면 재실행 방지
    if (window.__UIShader && window.__UIShader._initialized) return;

    // ═══════════════════════════════════════════════════════════════
    // 1. 상수 및 기본 설정
    // ═══════════════════════════════════════════════════════════════

    var DEPTH_CANVAS_ID = '__uishader_depth_canvas__';
    var CANVAS_SIZE = 512;
    var MIN_RENDER_AREA = 2;

    var EXCLUDED_TAGS = {
        SCRIPT: 1, STYLE: 1, META: 1, LINK: 1, HEAD: 1, TITLE: 1,
        NOSCRIPT: 1, BR: 1, HR: 1, TEMPLATE: 1, IFRAME: 1
    };

    // 정규화 상수
    var NORMALIZE = {
        maxZIndex: 9999,
        maxShadowBlur: 48,
        maxTransformZ: 100
    };

    // ═══════════════════════════════════════════════════════════════
    // 2. SignalRegistry — 확장 가능한 신호 추출기 레지스트리
    // ═══════════════════════════════════════════════════════════════
    //
    // 각 신호 추출기는 { name, weight, extract(element, style, ctx) } 형태.
    // extract는 0~1 사이의 정규화된 값을 반환해야 한다.
    // ctx에는 렌더 프레임 전역 데이터(maxDOMDepth 등)가 포함된다.

    var SignalRegistry = (function () {
        var signals = [];
        var signalMap = {};

        return {
            /**
             * 신호 추출기를 등록한다.
             * @param {string} name — 고유 식별자
             * @param {number} weight — 기본 가중치
             * @param {function} extractFn — (element, computedStyle, ctx) => 0~1
             */
            register: function (name, weight, extractFn) {
                if (signalMap[name]) {
                    // 기존 신호 교체
                    var existing = signalMap[name];
                    existing.weight = weight;
                    existing.extract = extractFn;
                    return;
                }
                var entry = { name: name, weight: weight, extract: extractFn };
                signals.push(entry);
                signalMap[name] = entry;
            },

            /** 등록된 모든 신호 목록 반환 */
            getAll: function () {
                return signals;
            },

            /** 이름으로 신호 조회 */
            get: function (name) {
                return signalMap[name] || null;
            },

            /** 신호 제거 */
            remove: function (name) {
                if (!signalMap[name]) return false;
                signals = signals.filter(function (s) { return s.name !== name; });
                delete signalMap[name];
                return true;
            },

            /** 가중치 일괄 업데이트 (JSON 객체) */
            updateWeights: function (weightMap) {
                for (var key in weightMap) {
                    if (weightMap.hasOwnProperty(key) && signalMap[key]) {
                        signalMap[key].weight = weightMap[key];
                    }
                }
            },

            /** 현재 가중치를 JSON 객체로 반환 */
            getWeights: function () {
                var result = {};
                for (var i = 0; i < signals.length; i++) {
                    result[signals[i].name] = signals[i].weight;
                }
                return result;
            },

            /** 가중치 합계 */
            weightSum: function () {
                var sum = 0;
                for (var i = 0; i < signals.length; i++) {
                    sum += signals[i].weight;
                }
                return sum;
            }
        };
    })();

    // ═══════════════════════════════════════════════════════════════
    // 3. 기본 신호 추출기 6종 등록
    // ═══════════════════════════════════════════════════════════════

    // --- Signal 1: DOM 중첩 깊이 (w1 = 0.25) ---
    // parentNode 체인 순회 → 계층 수가 깊을수록 전경에 가까움
    SignalRegistry.register('domDepth', 0.25, function (element, style, ctx) {
        var depth = 0;
        var el = element;
        while (el.parentElement) {
            el = el.parentElement;
            depth++;
        }
        return depth / Math.max(ctx.maxDOMDepth, 1);
    });

    // --- Signal 2: 쌓임 맥락 z-index (w2 = 0.25) ---
    // 명시적 z-index가 높을수록 시각적으로 앞에 위치
    // auto인 경우 부모 체인에서 가장 가까운 명시적 z-index 탐색
    SignalRegistry.register('stackContext', 0.25, function (element, style) {
        var z = parseInt(style.zIndex);

        if (isNaN(z)) {
            var parent = element.parentElement;
            while (parent) {
                var parentZ = parseInt(getComputedStyle(parent).zIndex);
                if (!isNaN(parentZ)) {
                    z = parentZ;
                    break;
                }
                parent = parent.parentElement;
            }
            if (isNaN(z)) return 0;
        }

        if (z < 0) return 0;
        return Math.min(z / NORMALIZE.maxZIndex, 1.0);
    });

    // --- Signal 3: Box Shadow 고도값 (w3 = 0.30, 최고 가중치) ---
    // Material Design elevation 시스템에 직접 대응.
    // blur 반경, Y 오프셋, spread를 복합 산출하여 고도 추정.
    // inset 그림자는 무시 (내부 그림자 = 깊이 기여 없음)
    SignalRegistry.register('boxShadow', 0.30, function (element, style) {
        var shadow = style.boxShadow;
        if (!shadow || shadow === 'none') return 0;

        var maxElevation = 0;
        // 다중 그림자를 쉼표로 분리 (색상 함수 내 쉼표는 괄호로 보호)
        var shadows = shadow.split(/,(?![^(]*\))/);

        for (var si = 0; si < shadows.length; si++) {
            var trimmed = shadows[si].trim();
            if (trimmed.indexOf('inset') !== -1) continue;

            var pxValues = [];
            var pxRegex = /(-?\d+(?:\.\d+)?)\s*px/g;
            var match;
            while ((match = pxRegex.exec(trimmed)) !== null) {
                pxValues.push(parseFloat(match[1]));
            }

            if (pxValues.length >= 3) {
                var offsetY = Math.abs(pxValues[1]);
                var blur = Math.abs(pxValues[2]);
                var spread = pxValues.length >= 4 ? Math.abs(pxValues[3]) : 0;
                // 복합 고도: blur 70% + offsetY 20% + spread 10%
                var elevation = blur * 0.7 + offsetY * 0.2 + spread * 0.1;
                if (elevation > maxElevation) maxElevation = elevation;
            }
        }

        return Math.min(maxElevation / NORMALIZE.maxShadowBlur, 1.0);
    });

    // --- Signal 4: Transform Z축 (w4 = 0.10) ---
    // matrix3d 인덱스 14 (m43) = translateZ
    SignalRegistry.register('transformZ', 0.10, function (element, style) {
        var transform = style.transform;
        if (!transform || transform === 'none') return 0;

        var match3d = transform.match(/matrix3d\((.+)\)/);
        if (match3d) {
            var values = match3d[1].split(',');
            if (values.length >= 15) {
                return Math.min(Math.abs(parseFloat(values[14])) / NORMALIZE.maxTransformZ, 1.0);
            }
        }
        return 0;
    });

    // --- Signal 5: 불투명도 힌트 (w5 = 0.05) ---
    // 완전 불투명(1.0) → 전경 가능성, 반투명 → 오버레이/배경막
    // 모호성이 높아 낮은 가중치
    SignalRegistry.register('opacity', 0.05, function (element, style) {
        var opacity = parseFloat(style.opacity);
        return isNaN(opacity) ? 1.0 : opacity;
    });

    // --- Signal 6: 배치 유형 (w6 = 0.05) ---
    // fixed/sticky → 최상위(1.0), absolute → 부유(0.6),
    // relative → 미세이동(0.3), static → 기본(0.0)
    SignalRegistry.register('position', 0.05, function (element, style) {
        switch (style.position) {
            case 'fixed': return 1.0;
            case 'sticky': return 1.0;
            case 'absolute': return 0.6;
            case 'relative': return 0.3;
            default: return 0.0;
        }
    });

    // ═══════════════════════════════════════════════════════════════
    // 4. DepthScorer — 복합 깊이 점수 산출
    // ═══════════════════════════════════════════════════════════════

    var DepthScorer = {
        /**
         * 요소의 복합 깊이 점수를 산출한다.
         * 등록된 모든 신호의 가중 합산 → 0~1 클램핑.
         */
        compute: function (element, style, ctx) {
            var signals = SignalRegistry.getAll();
            var score = 0;
            for (var i = 0; i < signals.length; i++) {
                var s = signals[i];
                if (s.weight <= 0) continue;
                score += s.weight * s.extract(element, style, ctx);
            }
            return Math.max(0, Math.min(score, 1.0));
        }
    };

    // ═══════════════════════════════════════════════════════════════
    // 5. DepthRenderer — DOM 순회 및 깊이 캔버스 렌더링
    // ═══════════════════════════════════════════════════════════════

    var DepthRenderer = (function () {
        var canvas, ctx;
        var stats = {
            lastRenderTimeMs: 0,
            elementsProcessed: 0,
            elementsSkipped: 0,
            maxDOMDepth: 1
        };

        function init() {
            canvas = document.getElementById(DEPTH_CANVAS_ID);
            if (!canvas) {
                canvas = document.createElement('canvas');
                canvas.id = DEPTH_CANVAS_ID;
                canvas.width = CANVAS_SIZE;
                canvas.height = CANVAS_SIZE;
                canvas.style.cssText = 'display:none!important;position:fixed!important;pointer-events:none!important;z-index:-99999!important;';
                document.documentElement.appendChild(canvas);
            }
            ctx = canvas.getContext('2d', { willReadFrequently: true });
        }

        /** DOM 트리 최대 깊이 사전 계산 */
        function calcMaxDOMDepth(el, depth) {
            if (depth > stats.maxDOMDepth) stats.maxDOMDepth = depth;
            var children = el.children;
            for (var i = 0, len = children.length; i < len; i++) {
                calcMaxDOMDepth(children[i], depth + 1);
            }
        }

        /** 전체 깊이 맵 렌더링 */
        function render() {
            if (!canvas || !ctx) init();

            var startTime = performance.now();
            stats.elementsProcessed = 0;
            stats.elementsSkipped = 0;
            stats.maxDOMDepth = 1;

            if (!document.body) return;

            // 1. 최대 DOM 깊이 사전 계산
            calcMaxDOMDepth(document.body, 0);

            // 2. 캔버스 초기화 (배경 = 깊이 0 = 검정)
            ctx.fillStyle = '#000000';
            ctx.fillRect(0, 0, CANVAS_SIZE, CANVAS_SIZE);

            // 3. 뷰포트 → 캔버스 변환 비율
            var vw = window.innerWidth || document.documentElement.clientWidth || 1;
            var vh = window.innerHeight || document.documentElement.clientHeight || 1;
            var scaleX = CANVAS_SIZE / vw;
            var scaleY = CANVAS_SIZE / vh;

            // 4. 렌더 프레임 컨텍스트 (신호 추출기에 전달)
            var renderCtx = { maxDOMDepth: stats.maxDOMDepth };

            // 5. DFS 순회 + 렌더링
            walkAndRender(document.body, scaleX, scaleY, vw, vh, renderCtx);

            stats.lastRenderTimeMs = performance.now() - startTime;
        }

        function walkAndRender(element, scaleX, scaleY, vw, vh, renderCtx) {
            if (EXCLUDED_TAGS[element.tagName]) return;

            var style;
            try {
                style = getComputedStyle(element);
            } catch (e) {
                return;
            }

            if (style.display === 'none' || style.visibility === 'hidden') {
                stats.elementsSkipped++;
                return;
            }

            var rect = element.getBoundingClientRect();

            // 뷰포트 내부이고 크기가 유효한 요소만 렌더링
            if (rect.width > 0 && rect.height > 0 &&
                rect.bottom > 0 && rect.top < vh &&
                rect.right > 0 && rect.left < vw) {

                var canvasX = rect.left * scaleX;
                var canvasY = rect.top * scaleY;
                var canvasW = rect.width * scaleX;
                var canvasH = rect.height * scaleY;

                if (canvasW >= MIN_RENDER_AREA || canvasH >= MIN_RENDER_AREA) {
                    var depth = DepthScorer.compute(element, style, renderCtx);
                    var brightness = Math.round(depth * 255);

                    ctx.fillStyle = 'rgb(' + brightness + ',' + brightness + ',' + brightness + ')';
                    ctx.fillRect(canvasX, canvasY, canvasW, canvasH);

                    stats.elementsProcessed++;
                }
            }

            // 자식 요소 순회 (자식이 부모 위에 그려짐 → 오버페인트)
            var children = element.children;
            for (var i = 0, len = children.length; i < len; i++) {
                walkAndRender(children[i], scaleX, scaleY, vw, vh, renderCtx);
            }
        }

        return {
            init: init,
            render: render,

            getCanvas: function () { return canvas; },
            getContext: function () { return ctx; },
            getStats: function () {
                return {
                    lastRenderTimeMs: stats.lastRenderTimeMs,
                    elementsProcessed: stats.elementsProcessed,
                    elementsSkipped: stats.elementsSkipped,
                    maxDOMDepth: stats.maxDOMDepth,
                    canvasSize: CANVAS_SIZE
                };
            }
        };
    })();

    // ═══════════════════════════════════════════════════════════════
    // 6. DOMWatcher — MutationObserver + 이벤트 기반 변경 감지
    // ═══════════════════════════════════════════════════════════════

    var DOMWatcher = (function () {
        var observer = null;
        var updateTimer = null;
        var scrollTimer = null;
        var animationPollId = null;
        var isWatching = false;
        var onDepthInvalidated = null; // 콜백

        var DEBOUNCE_MS = 50;
        var SCROLL_DEBOUNCE_MS = 100;
        var ANIMATION_POLL_MS = 100;
        var ANIMATION_CHECK_INTERVAL_MS = 5000;
        var animationCheckId = null;

        function scheduleUpdate() {
            if (updateTimer) clearTimeout(updateTimer);
            updateTimer = setTimeout(function () {
                requestAnimationFrame(function () {
                    if (onDepthInvalidated) onDepthInvalidated();
                });
            }, DEBOUNCE_MS);
        }

        function startMutationObserver() {
            if (!document.body) return;

            observer = new MutationObserver(function (mutations) {
                var shouldUpdate = false;
                for (var i = 0; i < mutations.length; i++) {
                    var m = mutations[i];
                    if (m.type === 'childList') {
                        shouldUpdate = true;
                        break;
                    }
                    if (m.type === 'attributes') {
                        var attr = m.attributeName;
                        if (attr === 'style' || attr === 'class' || attr === 'hidden') {
                            shouldUpdate = true;
                            break;
                        }
                    }
                }
                if (shouldUpdate) scheduleUpdate();
            });

            observer.observe(document.body, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['style', 'class', 'hidden']
            });
        }

        function startEventListeners() {
            // CSS transition 완료 — 깊이 관련 속성만 필터링
            document.addEventListener('transitionend', function (e) {
                var prop = e.propertyName;
                if (prop === 'box-shadow' || prop === 'transform' ||
                    prop === 'opacity' || prop === 'z-index' ||
                    prop === 'position' || prop === 'top' ||
                    prop === 'left' || prop === 'right' || prop === 'bottom') {
                    scheduleUpdate();
                }
            }, true);

            // CSS animation 완료
            document.addEventListener('animationend', function () {
                scheduleUpdate();
            }, true);

            // 스크롤 (디바운스 길게)
            window.addEventListener('scroll', function () {
                if (scrollTimer) clearTimeout(scrollTimer);
                scrollTimer = setTimeout(function () {
                    requestAnimationFrame(function () {
                        if (onDepthInvalidated) onDepthInvalidated();
                    });
                }, SCROLL_DEBOUNCE_MS);
            }, { passive: true });

            // 리사이즈
            window.addEventListener('resize', function () {
                scheduleUpdate();
            });
        }

        /** CSS 애니메이션 활성 상태 폴링 */
        function checkActiveAnimations() {
            // 성능: querySelectorAll('*') 대신 body부터 제한적 탐색
            var hasRunning = false;
            var elements = document.querySelectorAll('[style*="animation"], [class]');
            for (var i = 0; i < Math.min(elements.length, 200); i++) {
                try {
                    var s = getComputedStyle(elements[i]);
                    if (s.animationName && s.animationName !== 'none' &&
                        parseFloat(s.animationDuration) > 0 &&
                        s.animationPlayState === 'running') {
                        hasRunning = true;
                        break;
                    }
                } catch (e) { /* skip */ }
            }

            if (hasRunning && !animationPollId) {
                animationPollId = setInterval(function () {
                    if (onDepthInvalidated) onDepthInvalidated();
                }, ANIMATION_POLL_MS);
            } else if (!hasRunning && animationPollId) {
                clearInterval(animationPollId);
                animationPollId = null;
            }
        }

        return {
            /**
             * DOM 감시를 시작한다.
             * @param {function} callback — 깊이 맵 재렌더링이 필요할 때 호출
             */
            start: function (callback) {
                if (isWatching) return;
                onDepthInvalidated = callback;

                if (document.body) {
                    startMutationObserver();
                } else {
                    document.addEventListener('DOMContentLoaded', startMutationObserver);
                }
                startEventListeners();

                // 주기적 애니메이션 활성 상태 확인
                animationCheckId = setInterval(checkActiveAnimations, ANIMATION_CHECK_INTERVAL_MS);

                isWatching = true;
            },

            stop: function () {
                if (observer) {
                    observer.disconnect();
                    observer = null;
                }
                if (updateTimer) clearTimeout(updateTimer);
                if (scrollTimer) clearTimeout(scrollTimer);
                if (animationPollId) clearInterval(animationPollId);
                if (animationCheckId) clearInterval(animationCheckId);
                isWatching = false;
            },

            /** 디바운스 시간 변경 (ms) */
            setDebounce: function (ms) {
                DEBOUNCE_MS = Math.max(10, ms);
            },

            /** 스크롤 디바운스 시간 변경 (ms) */
            setScrollDebounce: function (ms) {
                SCROLL_DEBOUNCE_MS = Math.max(10, ms);
            },

            isActive: function () { return isWatching; }
        };
    })();

    // ═══════════════════════════════════════════════════════════════
    // 7. DepthExtractor — 오케스트레이터 + 공개 API
    // ═══════════════════════════════════════════════════════════════

    var depthDirty = false;
    var renderLock = false;
    var version = '2.0.0';

    function executeRender() {
        if (renderLock) return;
        renderLock = true;
        try {
            DepthRenderer.render();
            depthDirty = true;
        } finally {
            renderLock = false;
        }
    }

    // 초기화
    DepthRenderer.init();

    // DOM 감시 시작 — 변경 감지 시 executeRender 호출
    DOMWatcher.start(executeRender);

    // ═══════════════════════════════════════════════════════════════
    // 8. 공개 API (window.__UIShader)
    // ═══════════════════════════════════════════════════════════════
    //
    // Unity C#에서 CEF_ExecuteJS로 호출하는 인터페이스.
    // 모든 메서드는 동기적으로 호출 가능하다.

    window.__UIShader = {
        /** 엔진 버전 */
        version: version,

        // ─── 렌더링 제어 ─────────────────────────────

        /** 깊이 맵 렌더링 실행 */
        renderDepthMap: executeRender,

        /** 깊이 맵 변경 플래그 (Unity 폴링용) */
        get depthDirty() { return depthDirty; },
        set depthDirty(v) { depthDirty = v; },
        _depthDirty: false, // 레거시 호환

        /** 깊이 맵 변경 플래그를 확인하고 리셋한다 (원자적 조작) */
        consumeDepthDirty: function () {
            var wasDirty = depthDirty;
            depthDirty = false;
            this._depthDirty = false;
            return wasDirty;
        },

        // ─── 데이터 추출 ─────────────────────────────

        /** 깊이 캔버스를 PNG data URL로 반환 (디버그/프리뷰용) */
        getDepthCanvasData: function () {
            var canvas = DepthRenderer.getCanvas();
            return canvas ? canvas.toDataURL('image/png') : '';
        },

        /** 깊이 캔버스 픽셀 RGBA 데이터 (Unity 전송용: 512×512×4 = 1,048,576 바이트) */
        getDepthCanvasPixels: function () {
            var ctx = DepthRenderer.getContext();
            if (!ctx) return null;
            return ctx.getImageData(0, 0, CANVAS_SIZE, CANVAS_SIZE).data;
        },

        /** R 채널만 추출하여 공유 버퍼에 기록 (SharedArrayBuffer 최적화) */
        writeToSharedBuffer: function (sharedBuffer) {
            var ctx = DepthRenderer.getContext();
            if (!ctx) return;
            var imageData = ctx.getImageData(0, 0, CANVAS_SIZE, CANVAS_SIZE);
            var src = imageData.data;
            var dst = new Uint8Array(sharedBuffer);
            for (var i = 0, len = CANVAS_SIZE * CANVAS_SIZE; i < len; i++) {
                dst[i] = src[i * 4]; // R 채널
            }
        },

        // ─── 신호 관리 (확장성 핵심) ────────────────

        /**
         * 새로운 깊이 신호를 등록한다.
         * @param {string} name
         * @param {number} weight
         * @param {function} extractFn — (element, computedStyle, ctx) => 0~1
         */
        registerSignal: function (name, weight, extractFn) {
            SignalRegistry.register(name, weight, extractFn);
        },

        /** 신호를 제거한다 */
        removeSignal: function (name) {
            return SignalRegistry.remove(name);
        },

        /** 등록된 모든 신호 이름 반환 */
        getSignalNames: function () {
            return SignalRegistry.getAll().map(function (s) { return s.name; });
        },

        // ─── 가중치 제어 ─────────────────────────────

        /** 가중치 일괄 업데이트 */
        updateWeights: function (newWeights) {
            SignalRegistry.updateWeights(newWeights);
        },

        /** 현재 가중치 반환 */
        getWeights: function () {
            return SignalRegistry.getWeights();
        },

        /** 가중치를 1.0으로 정규화 */
        normalizeWeights: function () {
            var sum = SignalRegistry.weightSum();
            if (sum <= 0) return;
            var weights = SignalRegistry.getWeights();
            for (var key in weights) {
                if (weights.hasOwnProperty(key)) {
                    weights[key] = weights[key] / sum;
                }
            }
            SignalRegistry.updateWeights(weights);
        },

        // ─── 프리셋 ──────────────────────────────────

        presets: {
            balanced: {
                domDepth: 0.25, stackContext: 0.25, boxShadow: 0.30,
                transformZ: 0.10, opacity: 0.05, position: 0.05
            },
            materialDesign: {
                domDepth: 0.15, stackContext: 0.20, boxShadow: 0.40,
                transformZ: 0.10, opacity: 0.05, position: 0.10
            },
            flatDesign: {
                domDepth: 0.45, stackContext: 0.25, boxShadow: 0.05,
                transformZ: 0.05, opacity: 0.10, position: 0.10
            }
        },

        /** 프리셋 적용 */
        applyPreset: function (presetName) {
            var preset = this.presets[presetName];
            if (!preset) {
                console.warn('[UIShader] 알 수 없는 프리셋: ' + presetName);
                return false;
            }
            SignalRegistry.updateWeights(preset);
            executeRender();
            return true;
        },

        /** 커스텀 프리셋 등록 */
        addPreset: function (name, weights) {
            this.presets[name] = weights;
        },

        // ─── DOM 감시 제어 ───────────────────────────

        /** DOM 감시 중지 */
        stopWatching: function () { DOMWatcher.stop(); },

        /** DOM 감시 재시작 */
        startWatching: function () { DOMWatcher.start(executeRender); },

        /** 디바운스 시간 조정 (ms) */
        setDebounce: function (ms) { DOMWatcher.setDebounce(ms); },

        /** 스크롤 디바운스 시간 조정 (ms) */
        setScrollDebounce: function (ms) { DOMWatcher.setScrollDebounce(ms); },

        // ─── 설정 ────────────────────────────────────

        /** 정규화 상수 변경 */
        setNormalize: function (key, value) {
            if (NORMALIZE.hasOwnProperty(key)) {
                NORMALIZE[key] = value;
            }
        },

        /** 캔버스 크기 반환 */
        getCanvasSize: function () { return CANVAS_SIZE; },

        // ─── 성능 및 디버그 ──────────────────────────

        /** 성능 통계 */
        getStats: function () {
            var rendererStats = DepthRenderer.getStats();
            rendererStats.signalCount = SignalRegistry.getAll().length;
            rendererStats.weightSum = SignalRegistry.weightSum();
            rendererStats.domWatcherActive = DOMWatcher.isActive();
            return rendererStats;
        },

        /**
         * 특정 요소의 깊이 점수를 분석한다 (디버그용).
         * @param {Element} element
         * @returns {{ total: number, signals: Object }}
         */
        inspectElement: function (element) {
            if (!element) return null;
            var style = getComputedStyle(element);
            var ctx = { maxDOMDepth: DepthRenderer.getStats().maxDOMDepth };
            var signals = SignalRegistry.getAll();
            var breakdown = {};
            var total = 0;

            for (var i = 0; i < signals.length; i++) {
                var s = signals[i];
                var raw = s.extract(element, style, ctx);
                var weighted = s.weight * raw;
                breakdown[s.name] = {
                    raw: Math.round(raw * 1000) / 1000,
                    weight: s.weight,
                    weighted: Math.round(weighted * 1000) / 1000
                };
                total += weighted;
            }

            return {
                total: Math.max(0, Math.min(total, 1.0)),
                signals: breakdown
            };
        },

        /** 초기화 완료 플래그 */
        _initialized: false
    };

    // ═══════════════════════════════════════════════════════════════
    // 9. 초기 렌더링
    // ═══════════════════════════════════════════════════════════════

    function performInitialRender() {
        // CSS 애니메이션/트랜지션 완료 대기 후 렌더링
        setTimeout(function () {
            executeRender();
            window.__UIShader._initialized = true;
            window.__UIShader._depthDirty = true;
            console.log('[UIShader] depth-extractor v' + version + ' 초기화 완료 (' +
                DepthRenderer.getStats().elementsProcessed + ' elements, ' +
                DepthRenderer.getStats().lastRenderTimeMs.toFixed(1) + 'ms)');
        }, 200);
    }

    if (document.readyState === 'complete') {
        performInitialRender();
    } else {
        window.addEventListener('load', performInitialRender);
    }

})();
