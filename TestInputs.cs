// ═══════════════════════════════════════════════════════════════════════════
// TEST INPUTS & EXPECTED OUTPUTS — Emergency AI Platform
// Use these curl commands to test the running API
// ═══════════════════════════════════════════════════════════════════════════

// ── TEST 1: Fire (Text) ───────────────────────────────────────────────────

/*
curl -X POST https://localhost:5001/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "HELP! There is a huge fire at the apartment building on Oak Street and 3rd Avenue. Flames are coming out of windows on the 2nd and 3rd floors. I can hear people screaming inside!",
    "lat": 40.7128,
    "lon": -74.0060
  }'

EXPECTED OUTPUT:
{
  "incident_type": "fire",
  "severity_level": "critical",
  "entities": { "people_involved": 1, "hazards": "flames, smoke" },
  "dispatch_recommendation": {
    "units_required": ["fire", "ambulance"],
    "priority": 1
  }
}
*/

// ── TEST 2: Road Accident (Text) ──────────────────────────────────────────

/*
curl -X POST https://localhost:5001/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Two cars crashed at the intersection near the central mall. One driver seems unconscious. There is some fuel leaking from one of the vehicles."
  }'

EXPECTED OUTPUT:
{
  "incident_type": "accident",
  "severity_level": "high",
  "entities": { "people_involved": 2, "vehicles_involved": 2, "hazards": "fuel leak" },
  "dispatch_recommendation": {
    "units_required": ["police", "ambulance", "fire"],
    "priority": 2
  }
}
*/

// ── TEST 3: Medical (Text) ────────────────────────────────────────────────

/*
curl -X POST https://localhost:5001/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "An elderly man collapsed in Central Park near the fountain. He is breathing but unresponsive."
  }'

EXPECTED OUTPUT:
{
  "incident_type": "medical",
  "severity_level": "high",
  "entities": { "people_involved": 1 },
  "dispatch_recommendation": {
    "units_required": ["ambulance"],
    "priority": 2
  }
}
*/

// ── TEST 4: Hazard (Text) ─────────────────────────────────────────────────

/*
curl -X POST https://localhost:5001/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "A gas pipe burst on Elm Street behind the supermarket. Strong gas smell in the area. No injuries yet but people are evacuating."
  }'

EXPECTED OUTPUT:
{
  "incident_type": "hazard",
  "severity_level": "high",
  "entities": { "hazards": "gas leak" },
  "dispatch_recommendation": {
    "units_required": ["utility", "fire", "police"],
    "priority": 2
  }
}
*/

// ── TEST 5: Image (base64 fire scene) ────────────────────────────────────

/*
# Convert your image to base64 first:
# base64 -i fire_photo.jpg | tr -d '\n' > fire_base64.txt

curl -X POST https://localhost:5001/api/emergency/image \
  -H "Content-Type: application/json" \
  -d '{
    "base64Image": "<YOUR_BASE64_JPEG_HERE>",
    "mimeType": "image/jpeg",
    "lat": 51.5074,
    "lon": -0.1278
  }'
*/

// ── TEST 6: Image upload (multipart) ─────────────────────────────────────

/*
curl -X POST https://localhost:5001/api/emergency/image/upload \
  -F "file=@accident.jpg;type=image/jpeg" \
  -F "lat=48.8566" \
  -F "lon=2.3522"
*/

// ── TEST 7: Audio (base64 WAV) ────────────────────────────────────────────

/*
# Convert your audio to base64:
# base64 -i voice_report.wav | tr -d '\n' > audio_base64.txt

curl -X POST https://localhost:5001/api/emergency/audio \
  -H "Content-Type: application/json" \
  -d '{
    "base64Audio": "<YOUR_BASE64_WAV_HERE>",
    "mimeType": "audio/wav"
  }'
*/

// ── Health Check ──────────────────────────────────────────────────────────

/*
curl https://localhost:5001/api/emergency/health

EXPECTED:
{ "status": "healthy", "version": "1.0.0", "time": "..." }
*/
