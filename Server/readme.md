dotnet .\Server.dll -- ASPNETCORE_URLS=http://localhost:5051 ServerOptions__ListenLocalPort=5550 ServerOptions__SiloPort=5551 GatewayPort=5552

# Commands

### raft /
GET http://localhost:5109/

### /
GET http://localhost:5012/sessions/stateless-grain

### /
GET http://localhost:5012/sessions/replicated-grain

### /
GET http://localhost:5012/sessions/

### stress POST
POST http://localhost:5012/sessions/stress/10

### stress GET
GET http://localhost:5012/sessions/stress/10000

### stress PATCH
PATCH http://localhost:5012/sessions/stress/10000/10000

### delete
DELETE http://localhost:5012/sessions/237346DC-4D8A-4404-8972-76E2B4C80D32

### get
POST http://localhost:5012/sessions/get
Content-Type: application/json

{
"SessionId": "237346DC-4D8A-4404-8972-76E2B4C80D32",
"Sections": ["sec1", "sec2"]
}


### patch
PATCH http://localhost:5012/sessions
Content-Type: application/json

{
"SessionId": "237346DC-4D8A-4404-8972-76E2B4C80D32",
"TimeToLive": 3000,
"Sections": [
{
"key": "sec1",
"value": "dQ==",
"Version": 1
},
{
"key": "sec2",
"value": "dGVzZXQgdGVzZXQ=",
"Version": 1
}
]
}