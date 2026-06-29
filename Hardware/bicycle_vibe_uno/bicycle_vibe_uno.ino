// ================================================================
//  [사용 중단] 자전거 시뮬레이터 — 진동 제어 (Arduino Uno)
//
//  v5.8부터 진동 제어가 ESP32-S3(bicycle_sim_x)로 통합되었습니다.
//  이 파일은 참고용으로만 보관합니다. 실제 배포에 사용하지 마세요.
//
//  대체: bicycle_sim_x v5.8 + IRF520 모듈 (GPIO2)
//
//  핀 연결:
//    Uno USB   ← Unity PC (COM 포트 별도)   [9600 bps]
//    Uno D9    → 트랜지스터 Base [1kΩ]
//    트랜지스터 Collector → 모터(-) + 다이오드 캐소드
//    트랜지스터 Emitter   → GND
//    모터(+) / 다이오드 애노드 → 5V
//
//  명령 포맷 (Unity → Uno):
//    V0 : 정지
//    V1 : Danger  (위험)
//    V2 : Success (성공)
//    V3 : Correct (정답)
//    V4 : Wrong   (오답)
//    V5 : Walk    (횡단보도)
//    V6 : Ready   (DMP 안정화)
//    B1 : 브레이크 ON  (연속 진동)
//    B0 : 브레이크 OFF
// ================================================================

#define PIN_MOTOR 9  // PWM 가능 핀

struct VibeStep { uint8_t s; uint16_t ms; };

const VibeStep PAT_DANGER[]  = {{255, 700}, {0, 0}};
const VibeStep PAT_SUCCESS[] = {{220, 150}, {0, 80}, {220, 150}, {0, 0}};
const VibeStep PAT_CORRECT[] = {{180, 100}, {0, 0}};
const VibeStep PAT_WRONG[]   = {{255, 450}, {0, 0}};
const VibeStep PAT_WALK[]    = {{200, 100}, {0, 80}, {200, 100}, {0, 80}, {200, 100}, {0, 0}};
const VibeStep PAT_READY[]   = {{160, 80},  {0, 60}, {200, 120}, {0, 0}};

static VibeStep g_seq[10];
static int      g_count = 0, g_idx = 0;
static uint32_t g_end   = 0;
static bool     g_brake = false;

void vibeSet(uint8_t val) { analogWrite(PIN_MOTOR, val); }

void playVibe(const VibeStep *p, int n)
{
    if (n <= 0 || n > 10) return;
    memcpy(g_seq, p, (size_t)n * sizeof(VibeStep));
    g_count = n; g_idx = 0;
    g_end = millis() + p[0].ms;
    vibeSet(p[0].s);
}

void vibeStop() { g_count = g_idx = 0; vibeSet(0); }
bool vibeIsPlaying() { return g_idx < g_count; }

void updateVibe()
{
    if (g_idx >= g_count) return;
    if ((uint32_t)(millis() - g_end) >= 0x80000000UL) return;
    if (++g_idx >= g_count) { vibeSet(g_brake ? 180 : 0); return; }
    vibeSet(g_seq[g_idx].s);
    g_end = millis() + g_seq[g_idx].ms;
}

void handleCommand(const String &cmd)
{
    if (cmd.length() < 1) return;
    char t = cmd.charAt(0);
    int  v = (cmd.length() > 1) ? cmd.substring(1).toInt() : 0;

    if (t == 'B')
    {
        g_brake = (v == 1);
        if (!vibeIsPlaying()) vibeSet(g_brake ? 180 : 0);
        return;
    }
    if (t != 'V') return;

    switch (v)
    {
        case 0: vibeStop();                                   break;
        case 1: playVibe(PAT_DANGER,  sizeof(PAT_DANGER)  / sizeof(VibeStep)); break;
        case 2: playVibe(PAT_SUCCESS, sizeof(PAT_SUCCESS) / sizeof(VibeStep)); break;
        case 3: playVibe(PAT_CORRECT, sizeof(PAT_CORRECT) / sizeof(VibeStep)); break;
        case 4: playVibe(PAT_WRONG,   sizeof(PAT_WRONG)   / sizeof(VibeStep)); break;
        case 5: playVibe(PAT_WALK,    sizeof(PAT_WALK)    / sizeof(VibeStep)); break;
        case 6: playVibe(PAT_READY,   sizeof(PAT_READY)   / sizeof(VibeStep)); break;
    }
}

String rxBuf = "";

void setup()
{
    Serial.begin(9600);
    pinMode(PIN_MOTOR, OUTPUT);
    vibeSet(0);
}

void loop()
{
    updateVibe();

    while (Serial.available())
    {
        char c = (char)Serial.read();
        if (c == '\n' || c == '\r')
        {
            rxBuf.trim();
            if (rxBuf.length() > 0) handleCommand(rxBuf);
            rxBuf = "";
        }
        else if (rxBuf.length() < 8)
        {
            rxBuf += c;
        }
    }
}
