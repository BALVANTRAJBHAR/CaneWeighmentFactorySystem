import os
import pytest
import requests

BASE_URL = os.environ.get("CANE_API_URL", "http://localhost:8001")


@pytest.fixture(scope="session")
def base_url():
    return BASE_URL


@pytest.fixture(scope="session")
def session():
    s = requests.Session()
    s.headers.update({"Content-Type": "application/json"})
    return s
