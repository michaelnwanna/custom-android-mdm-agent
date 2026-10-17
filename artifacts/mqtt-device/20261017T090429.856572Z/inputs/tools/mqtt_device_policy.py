import hashlib
import hmac
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone

from cryptography import x509
from cryptography.exceptions import UnsupportedAlgorithm
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import ExtendedKeyUsageOID, SignatureAlgorithmOID


@dataclass(frozen=True)
class DeviceMembership:
    certificate_sha256: str
    enabled: bool


def canonical_device_id(value):
    if not isinstance(value, str):
        raise ValueError("Invalid device identity")
    parsed = uuid.UUID(value)
    if parsed.int == 0 or str(parsed) != value:
        raise ValueError("Invalid device identity")
    return value


def authenticate_certificate(certificate_der, client_id, memberships, now=None):
    try:
        device_id = canonical_device_id(client_id)
        membership = memberships.get(device_id)
        if membership is None or not membership.enabled:
            return None
        digest = hashlib.sha256(certificate_der).hexdigest()
        if not hmac.compare_digest(digest, membership.certificate_sha256):
            return None
        certificate = x509.load_der_x509_certificate(certificate_der)
        if certificate.public_bytes(serialization.Encoding.DER) != certificate_der:
            return None
        current = now or datetime.now(timezone.utc)
        if not certificate.not_valid_before_utc <= current < certificate.not_valid_after_utc:
            return None
        if (certificate.not_valid_after_utc - certificate.not_valid_before_utc).total_seconds() > 172800:
            return None
        key = certificate.public_key()
        if not isinstance(key, ec.EllipticCurvePublicKey) or not isinstance(key.curve, ec.SECP256R1):
            return None
        if certificate.signature_algorithm_oid != SignatureAlgorithmOID.ECDSA_WITH_SHA256:
            return None
        if certificate.extensions.get_extension_for_class(x509.BasicConstraints).value.ca:
            return None
        names = list(certificate.extensions.get_extension_for_class(x509.SubjectAlternativeName).value)
        if names != [x509.UniformResourceIdentifier("urn:fleet:device:" + device_id)]:
            return None
        usages = certificate.extensions.get_extension_for_class(x509.KeyUsage).value
        if not usages.digital_signature or any((usages.content_commitment, usages.key_encipherment,
                                                usages.data_encipherment, usages.key_agreement,
                                                usages.key_cert_sign, usages.crl_sign)):
            return None
        purposes = list(certificate.extensions.get_extension_for_class(x509.ExtendedKeyUsage).value)
        if purposes != [ExtendedKeyUsageOID.CLIENT_AUTH]:
            return None
        return device_id
    except (ValueError, TypeError, UnsupportedAlgorithm, x509.ExtensionNotFound, x509.DuplicateExtension):
        return None


def authorize_topic(device_id, topic, action):
    try:
        canonical_device_id(device_id)
    except (ValueError, TypeError, AttributeError):
        return False
    prefix = "fleet/devices/" + device_id + "/"
    if action == "publish":
        return topic == prefix + "command-events"
    if action in ("subscribe", "receive"):
        return topic in (prefix + "commands", prefix + "event-acks")
    return False