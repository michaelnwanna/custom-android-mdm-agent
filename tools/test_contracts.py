import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import OpenAPIV31SpecValidator


ROOT = Path(__file__).resolve().parents[1]


class ContractTests(unittest.TestCase):
    def test_device_identity_requires_mtls_and_no_bootstrap_body(self):
        document = json.loads((ROOT / "contracts/openapi.json").read_text())
        operation = document["paths"]["/api/v1/device/identity"]["get"]
        self.assertEqual(operation["security"], [{"deviceCertificate": []}])
        self.assertNotIn("requestBody", operation)
        self.assertEqual(set(operation["responses"]), {"200", "400", "401", "403", "429", "500", "503"})
        self.assertEqual(operation["responses"]["200"]["content"]["application/json"]["schema"]["$ref"],
                         "./http/device-identity-v1.schema.json")
        self.assertNotIn("WWW-Authenticate", operation["responses"]["401"]["headers"])

    def test_all_declared_errors_fit_problem_schema(self):
        document = json.loads((ROOT / "contracts/openapi.json").read_text())
        schema = json.loads((ROOT / "contracts/http/problem-v1.schema.json").read_text())
        validator = Draft202012Validator(schema)
        for item in document["paths"].values():
            for operation in item.values():
                for status in operation["responses"]:
                    if int(status) >= 400:
                        validator.validate({"type": "about:blank", "title": "Generic error", "status": int(status)})

    def test_pending_enrollment_administrator_boundary(self):
        document = json.loads((ROOT / "contracts/openapi.json").read_text())
        operation = document["paths"]["/api/v1/admin/pending-enrollments"]["post"]
        self.assertEqual(operation["security"], [{"administratorAccessToken": []}])
        self.assertEqual(set(operation["responses"]), {"201", "400", "401", "403", "413", "415", "429", "500", "503"})
        self.assertEqual(operation["requestBody"]["content"]["application/json"]["schema"]["$ref"],
                         "./http/pending-enrollment-request-v1.schema.json")
        self.assertEqual(operation["responses"]["201"]["content"]["application/json"]["schema"]["$ref"],
                         "./http/pending-enrollment-response-v1.schema.json")
        self.assertEqual(operation["responses"]["201"]["headers"]["Cache-Control"]["$ref"],
                         "#/components/headers/NoStore")

    def test_enrollment_request_size_bounds(self):
        schema = json.loads((ROOT / "contracts/http/enrollment-request-v1.schema.json").read_text())
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        request = {
            "schemaVersion": 1,
            "pendingEnrollmentId": "a9373871-de89-4723-bc55-884e76916e67",
            "csrDer": "A" * 16384,
        }
        validator.validate(request)
        for length in (60, 16388):
            with self.subTest(length=length):
                request["csrDer"] = "A" * length
                self.assertTrue(list(validator.iter_errors(request)))

    def test_enrollment_response_chain_bounds(self):
        schema = json.loads((ROOT / "contracts/http/enrollment-response-v1.schema.json").read_text())
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        suite = json.loads((ROOT / "contracts/fixtures/enrollment-responses-v1.json").read_text())
        response = copy.deepcopy(suite["valid"][0])
        response["intermediateCertificatesDer"] = [character * 64 for character in "ABCD"]
        validator.validate(response)
        for chain in (["A" * 64] * 2, [character * 64 for character in "ABCDE"]):
            with self.subTest(chain=chain):
                response["intermediateCertificatesDer"] = chain
                self.assertTrue(list(validator.iter_errors(response)))

    def test_openapi_document(self):
        filename = ROOT / "contracts" / "openapi.json"
        document = json.loads(filename.read_text())
        OpenAPIV31SpecValidator(document, base_uri=filename.as_uri()).validate()

    def test_enrollment_http_boundaries(self):
        document = json.loads((ROOT / "contracts" / "openapi.json").read_text())
        operation = document["paths"]["/api/v1/enrollments"]["post"]
        self.assertEqual(operation["security"], [{"enrollmentToken": []}])
        self.assertEqual(document["security"], [{"deviceCertificate": []}])
        self.assertEqual(set(operation["responses"]), {"200", "201", "400", "401", "409", "413", "415", "429", "500", "503"})
        self.assertEqual(operation["requestBody"]["content"]["application/json"]["schema"]["$ref"],
                         "./http/enrollment-request-v1.schema.json")
        for status in ("200", "201"):
            response = operation["responses"][status]
            self.assertEqual(response["content"]["application/json"]["schema"]["$ref"],
                             "./http/enrollment-response-v1.schema.json")
            self.assertEqual(response["headers"]["Cache-Control"]["$ref"], "#/components/headers/NoStore")
        self.assertEqual(operation["responses"]["401"]["headers"]["WWW-Authenticate"]["schema"]["const"], "Bearer")
        for status in ("429", "503"):
            self.assertIn("Retry-After", operation["responses"][status]["headers"])

    def test_every_schema_has_shared_fixtures(self):
        schemas = {
            str(filename.relative_to(ROOT / "contracts"))
            for filename in (ROOT / "contracts").rglob("*.schema.json")
        }
        fixtures = {
            json.loads(filename.read_text())["schema"]
            for filename in (ROOT / "contracts" / "fixtures").glob("*-v1.json")
        }
        self.assertEqual(schemas, fixtures)

    def test_shared_fixtures(self):
        fixtures = sorted((ROOT / "contracts" / "fixtures").glob("*-v1.json"))
        self.assertTrue(fixtures, "No contract fixture suites found")
        for filename in fixtures:
            suite = json.loads(filename.read_text())
            schema = json.loads((ROOT / "contracts" / suite["schema"]).read_text())
            Draft202012Validator.check_schema(schema)
            validator = Draft202012Validator(schema, format_checker=FormatChecker())
            self.assertTrue(suite["valid"], filename.name)
            self.assertTrue(suite["invalid"], filename.name)
            for index, instance in enumerate(suite["valid"]):
                with self.subTest(suite=filename.name, valid=index):
                    validator.validate(instance)
            for case in suite["invalid"]:
                with self.subTest(suite=filename.name, invalid=case["name"]):
                    instance = copy.deepcopy(suite["valid"][case["base"]])
                    instance.update(case.get("set", {}))
                    for field in case.get("remove", []):
                        del instance[field]
                    self.assertTrue(list(validator.iter_errors(instance)), case["name"])
            print(f"CASES {filename.name}: {len(suite['valid'])} valid, {len(suite['invalid'])} rejection cases")


if __name__ == "__main__":
    unittest.main()