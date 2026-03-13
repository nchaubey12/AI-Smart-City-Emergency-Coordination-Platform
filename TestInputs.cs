// ═══════════════════════════════════════════════════════════════════════════
// TEST INPUTS & EXPECTED OUTPUTS — Emergency AI Platform
// Use these curl commands to test the running API at http://localhost:5000
// ═══════════════════════════════════════════════════════════════════════════

// ── TEST 1: Fire — Apartment Building ────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "HELP! There is a huge fire at the apartment building on Oak Street and 3rd Avenue. Flames are coming out of windows on the 2nd and 3rd floors. I can hear people screaming inside!",
    "lat": 40.7128,
    "lon": -74.0060
  }'

EXPECTED:
{
  "incident_type": "fire",
  "severity_level": "critical",
  "entities": { "people_involved": 2, "hazards": "flames, smoke" },
  "dispatch_recommendation": {
    "units_required": ["fire", "ambulance", "police"],
    "priority": 1
  }
}
*/

// ── TEST 2: Major Highway Accident — Mass Casualty ────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Accident at the main highway, many people injured, cars are in flames, huge traffic jam, need urgent help",
    "lat": 49.2160,
    "lon": 12.7389
  }'

EXPECTED:
{
  "incident_type": "accident",
  "severity_level": "critical",
  "entities": {
    "people_involved": 10,
    "vehicles_involved": 3,
    "hazards": "flames, smoke"
  },
  "dispatch_recommendation": {
    "units_required": ["police", "traffic_police", "ambulance", "fire", "tow_truck"],
    "priority": 1
  }
}
*/

// ── TEST 3: Highway Multi-Car Pileup ─────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "There has been a major pileup on the highway near exit 12. At least 6 cars involved. Several people are trapped inside the vehicles. Traffic is completely blocked.",
    "lat": 48.1351,
    "lon": 11.5820
  }'

EXPECTED:
{
  "incident_type": "accident",
  "severity_level": "critical",
  "entities": {
    "people_involved": 6,
    "vehicles_involved": 6,
    "hazards": ""
  },
  "dispatch_recommendation": {
    "units_required": ["police", "traffic_police", "ambulance", "fire", "tow_truck"],
    "priority": 1
  }
}
*/

// ── TEST 4: Road Accident — Fuel Leak ────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Two cars crashed at the intersection near the central mall. One driver seems unconscious. There is some fuel leaking from one of the vehicles.",
    "lat": 51.5074,
    "lon": -0.1278
  }'

EXPECTED:
{
  "incident_type": "accident",
  "severity_level": "high",
  "entities": {
    "people_involved": 2,
    "vehicles_involved": 2,
    "hazards": "fuel leak"
  },
  "dispatch_recommendation": {
    "units_required": ["police", "ambulance", "fire", "tow_truck"],
    "priority": 2
  }
}
*/

// ── TEST 5: Medical — Heart Attack ────────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "My friend just had a heart attack, he is unconscious and not breathing properly, we are at the city park near the main entrance",
    "lat": 48.8566,
    "lon": 2.3522
  }'

EXPECTED:
{
  "incident_type": "medical",
  "severity_level": "critical",
  "entities": { "people_involved": 1 },
  "dispatch_recommendation": {
    "units_required": ["ambulance", "police"],
    "priority": 1
  }
}
*/

// ── TEST 6: Medical — Elderly Person Collapsed ───────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "An elderly man collapsed in Central Park near the fountain. He is breathing but unresponsive."
  }'

EXPECTED:
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

// ── TEST 7: Medical — Mass Casualty Event ────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "There has been a stampede at the football stadium. More than 30 people are injured, some are unconscious. People are panicking.",
    "lat": 52.5200,
    "lon": 13.4050
  }'

EXPECTED:
{
  "incident_type": "medical",
  "severity_level": "critical",
  "entities": { "people_involved": 30 },
  "dispatch_recommendation": {
    "units_required": ["ambulance", "police"],
    "priority": 1
  }
}
*/

// ── TEST 8: Hazard — Gas Leak ─────────────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "A gas pipe burst on Elm Street behind the supermarket. Strong gas smell in the area. No injuries yet but people are evacuating.",
    "lat": 53.4808,
    "lon": -2.2426
  }'

EXPECTED:
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

// ── TEST 9: Crime — Armed Robbery ────────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "There is an armed robbery happening at the bank on Main Street. Two men with guns. Customers are being held inside.",
    "lat": 40.7580,
    "lon": -73.9855
  }'

EXPECTED:
{
  "incident_type": "crime",
  "severity_level": "critical",
  "entities": { "people_involved": 3 },
  "dispatch_recommendation": {
    "units_required": ["police"],
    "priority": 1
  }
}
*/

// ── TEST 10: Crime — Fight with Knife ────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Big fight outside the shopping mall, someone has a knife, one person is bleeding badly."
  }'

EXPECTED:
{
  "incident_type": "crime",
  "severity_level": "critical",
  "entities": { "people_involved": 2, "hazards": "" },
  "dispatch_recommendation": {
    "units_required": ["police", "ambulance"],
    "priority": 1
  }
}
*/

// ── TEST 11: Fire — Vehicle Fire on Road ─────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "A truck is on fire on the motorway, blocking two lanes. The driver managed to get out but the fire is spreading to nearby bushes.",
    "lat": 51.4545,
    "lon": -2.5879
  }'

EXPECTED:
{
  "incident_type": "fire",
  "severity_level": "high",
  "entities": { "vehicles_involved": 1, "hazards": "flames, smoke" },
  "dispatch_recommendation": {
    "units_required": ["fire", "ambulance", "police", "traffic_police", "tow_truck"],
    "priority": 2
  }
}
*/

// ── TEST 12: Hazard — Flooding ────────────────────────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "The river has overflowed and is flooding the residential area on Riverside Road. Several houses are flooded up to the first floor. Families are trapped on rooftops.",
    "lat": 47.3769,
    "lon": 8.5417
  }'

EXPECTED:
{
  "incident_type": "hazard",
  "severity_level": "critical",
  "entities": { "hazards": "flooding" },
  "dispatch_recommendation": {
    "units_required": ["utility", "fire", "police", "ambulance"],
    "priority": 1
  }
}
*/

// ── TEST 13: Low Severity — Minor Fender Bender ──────────────────────────

/*
curl -X POST http://localhost:5000/api/emergency/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Two cars had a minor collision in the parking lot. No one is injured. Just some scratches on the bumpers. We need police to document the accident."
  }'

EXPECTED:
{
  "incident_type": "accident",
  "severity_level": "low",
  "entities": { "vehicles_involved": 2 },
  "dispatch_recommendation": {
    "units_required": ["police"],
    "priority": 4
  }
}
*/

// ── Health Check ──────────────────────────────────────────────────────────

/*
curl http://localhost:5000/api/emergency/health

EXPECTED:
{ "status": "healthy", "version": "1.0.0", "time": "..." }
*/