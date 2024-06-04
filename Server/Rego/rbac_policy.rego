package services_rbac

import rego.v1

# доступы сервисов к секциям сессий
# c - create, r - read, u - update, i - invalidate
service_permissions := {
    "service_1": {"user_info": ["r", "u"] },
    "service_2": {"camera_translation_token": ["r"] },
    "service_3": {"camera_ptz_token": ["r", "u"], "active_transaction": ["c", "r", "u"]}
}

service_session_permissions := {
    "session_creation_service": ["c"],
    "session_creation_service": ["i"],
}

# role based access control
default allow := false
allow if {
    permissions := service_permissions[input.service_id]
    p := permissions[_]
    p == {input.object: input.action}
}

# запрос
#{
#  "service_id": "service_1",
#  "action": "r",
#  "section": "user_info"
#}

# ответ
# true