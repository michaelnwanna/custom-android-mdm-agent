import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker


ROOT = Path(__file__).resolve().parents[1]


class ContractTests(unittest.TestCase):
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