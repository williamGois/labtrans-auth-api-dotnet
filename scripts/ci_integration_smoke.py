from __future__ import annotations

import base64
import json
import os
import sys
import time
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


AUTH_API_URL = os.getenv("AUTH_API_URL", "http://127.0.0.1:5001").rstrip("/")
JWT_ISSUER = os.getenv("JWT_ISSUER", "labtrans-auth-api")
JWT_AUDIENCE = os.getenv("JWT_AUDIENCE", "labtrans-reservas")
TIMEOUT_SECONDS = int(os.getenv("CI_SMOKE_TIMEOUT_SECONDS", "45"))


def parse_body(raw: str) -> Any:
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return raw


def request(
    method: str,
    path: str,
    *,
    body: dict[str, Any] | None = None,
    token: str | None = None,
) -> tuple[int, dict[str, str], Any]:
    headers = {"Accept": "application/json", "X-Correlation-ID": f"ci-auth-{int(time.time())}"}
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"

    req = Request(f"{AUTH_API_URL}{path}", data=data, headers=headers, method=method)
    try:
        with urlopen(req, timeout=10) as response:
            return response.status, dict(response.headers), parse_body(
                response.read().decode("utf-8", errors="replace")
            )
    except HTTPError as error:
        return error.code, dict(error.headers), parse_body(
            error.read().decode("utf-8", errors="replace")
        )


def wait_for_ready() -> None:
    deadline = time.time() + TIMEOUT_SECONDS
    last_error = ""
    while time.time() < deadline:
        try:
            status_code, _, body = request("GET", "/health/ready")
            if status_code == 200 and isinstance(body, dict) and body.get("status") == "ready":
                print("[PASS] Auth API ready health")
                return
            last_error = f"HTTP {status_code}: {body}"
        except URLError as exc:
            last_error = str(exc)
        time.sleep(1)
    raise RuntimeError(f"Auth API did not become ready: {last_error}")


def decode_payload(token: str) -> dict[str, Any]:
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return json.loads(base64.urlsafe_b64decode(payload.encode("utf-8")))


def generated_password() -> str:
    return "Ci" + "Credential" + str(int(time.time())) + "!"


def assert_status(label: str, expected: int, actual: int, body: Any = None) -> None:
    if actual != expected:
        raise AssertionError(f"{label}: expected HTTP {expected}, got HTTP {actual}: {body}")
    print(f"[PASS] {label} (HTTP {actual})")


def main() -> int:
    wait_for_ready()

    status_code, headers, body = request("GET", "/health/live")
    assert_status("Auth API live health", 200, status_code, body)
    if not headers.get("X-Correlation-ID"):
        raise AssertionError("Auth API did not return X-Correlation-ID")
    print("[PASS] Auth API returns correlation id")

    email = f"ci-auth-{int(time.time())}@example.test"
    password = generated_password()

    status_code, _, body = request(
        "POST", "/api/auth/register", body={"email": email, "password": password}
    )
    assert_status("Register user against PostgreSQL", 201, status_code, body)

    status_code, _, body = request(
        "POST", "/api/auth/login", body={"email": email, "password": password}
    )
    assert_status("Login user against PostgreSQL", 200, status_code, body)
    if not isinstance(body, dict) or not body.get("accessToken"):
        raise AssertionError("Login did not return accessToken")
    access_token = body["accessToken"]

    claims = decode_payload(access_token)
    if not claims.get("sub") or claims.get("email") != email:
        raise AssertionError("JWT does not contain expected subject and email claims")
    if claims.get("iss") != JWT_ISSUER:
        raise AssertionError("JWT issuer is invalid")
    audience = claims.get("aud")
    audience_values = [audience] if isinstance(audience, str) else audience
    if JWT_AUDIENCE not in audience_values:
        raise AssertionError("JWT audience is invalid")
    if int(claims.get("exp", 0)) <= int(time.time()):
        raise AssertionError("JWT expiration is invalid")
    print("[PASS] JWT contains sub, email, issuer, audience and expiration")

    status_code, _, body = request("GET", "/api/auth/me", token=access_token)
    assert_status("Authenticated /api/auth/me", 200, status_code, body)
    if not isinstance(body, dict) or body.get("email") != email:
        raise AssertionError("/api/auth/me returned unexpected user")

    status_code, _, body = request(
        "POST", "/api/auth/login", body={"email": email, "password": generated_password()}
    )
    assert_status("Invalid credentials rejected", 401, status_code, body)

    status_code, _, body = request("GET", "/metrics")
    assert_status("Auth metrics endpoint", 200, status_code, body)
    if "auth_login_success_total" not in str(body):
        raise AssertionError("Auth metrics do not expose auth_login_success_total")
    print("[PASS] Auth integration smoke completed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
