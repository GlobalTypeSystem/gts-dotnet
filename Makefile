CI := 1

# Default target - show help
.DEFAULT_GOAL := help

.PHONY: help build dev-fmt all check fmt lint test security coverage clean

# Solution and the CLI project that produces the `gts` binary.
SLN := gts-dotnet.sln
CLI_PROJECT := Gts.Cli/Gts.Cli.csproj
CONFIG ?= Release

# Show this help message
help:
	@awk '/^# / { desc=substr($$0, 3) } /^[a-zA-Z0-9_-]+:/ && desc { target=$$1; sub(/:$$/, "", target); printf "%-20s - %s\n", target, desc; desc="" }' Makefile | sort

# Build the solution and publish the gts CLI binary to ./bin/gts
build:
	dotnet build $(SLN) -c $(CONFIG)
	dotnet publish $(CLI_PROJECT) -c $(CONFIG) -o ./bin

# Fix formatting issues
dev-fmt:
	dotnet format $(SLN)

# Run all checks and build
all: check build

# Check code formatting
fmt:
	dotnet format whitespace $(SLN) --verify-no-changes
	dotnet format style $(SLN) --verify-no-changes

# Run analyzers (linter)
lint:
	dotnet format analyzers $(SLN) --verify-no-changes

# Run all tests
test:
	dotnet test $(SLN) -c $(CONFIG)

# Check for vulnerable NuGet dependencies
security:
	dotnet list $(SLN) package --vulnerable --include-transitive

# Measure code coverage
coverage:
	dotnet test $(SLN) -c $(CONFIG) --collect:"XPlat Code Coverage"
	@echo "Coverage reports written under */TestResults/*/coverage.cobertura.xml"

# Remove build artifacts
clean:
	dotnet clean $(SLN)
	rm -rf ./bin

# Run all quality checks
check: fmt lint test gts-spec-tests


# ==============================================================================
# gts-spec conformance tests
# ==============================================================================
#
# Two flows:
#
#   * `make gts-spec-tests` — one-shot: builds the gts binary, pulls the
#     test-runner image, starts the server, runs pytest, shuts the server down.
#     This is what CI runs.
#
#   * `make gts-server` + `make gts-spec-tests-run` — long-running server in
#     one terminal, repeated test runs in another. For iterative test work.
#
# Both use the gts-spec test-runner image from GHCR; the tag comes from
# .gts-spec-version (used verbatim, format `vMAJOR.MINOR.PATCH`).

.PHONY: gts-server gts-spec-tests gts-spec-tests-run gts-spec-tests-pull gts-spec-version-check

# Port for the reference server; override with `PORT=8001`
PORT ?= 8000

# gts-spec test-runner image. The tag is read from `.gts-spec-version`
# (canonical pin for this implementation, format `vMAJOR.MINOR.PATCH` — an
# immutable patch tag so every commit is reproducible). Override either
# variable to follow a rolling minor tag or test a fork:
#   make gts-spec-tests GTS_SPEC_VERSION=v0.11
#   make gts-spec-tests GTS_SPEC_IMAGE=ghcr.io/your-fork/gts-spec-tests
GTS_SPEC_IMAGE ?= ghcr.io/globaltypesystem/gts-spec-tests
GTS_SPEC_VERSION ?= $(strip $(shell cat .gts-spec-version 2>/dev/null))
GTS_SPEC_REF ?= $(GTS_SPEC_IMAGE):$(GTS_SPEC_VERSION)

# Optional: path to a local checkout of gts-spec/tests to bind-mount over
# /tests inside the runner image. Useful when iterating on the test suite
# itself alongside the server, e.g.:
#   make gts-spec-tests GTS_SPEC_TESTS_DIR=../gts-spec/tests
GTS_SPEC_TESTS_DIR ?=

# Optional pytest selector passed straight to the runner. Examples:
#   make gts-spec-tests TEST=test_op1_id_validation.py
#   make gts-spec-tests TEST=test_op12_type_derivation_validation.py::TestCaseOp12_FinalBase_RejectDerived
TEST ?=

# Seconds to wait for the GTS server to start accepting requests.
SERVER_READY_TIMEOUT ?= 10

# Shell snippet (expanded inline) that dumps the last 50 lines of .server.log
# to stderr if the file exists and is non-empty. Reused by error paths so the
# operator sees *why* the server failed rather than just "did not respond".
DUMP_SERVER_LOG = \
	if [ -s .server.log ]; then \
		echo "=== last 50 lines of .server.log ==="; \
		tail -n 50 .server.log; \
		echo "=== end .server.log ==="; \
	fi

# Shell snippet (expanded inline) that polls /entities every 200 ms until the
# server answers, or fails after $(SERVER_READY_TIMEOUT) seconds. Assumes the
# server PID has been written to .server.pid and stdout/stderr to .server.log
# by the caller.
WAIT_FOR_SERVER = \
	echo "Waiting up to $(SERVER_READY_TIMEOUT)s for server on :$(PORT)..."; \
	attempts=$$(( $(SERVER_READY_TIMEOUT) * 5 )); \
	for i in $$(seq 1 $$attempts); do \
		if ! kill -0 $$(cat .server.pid) 2>/dev/null; then \
			echo "ERROR: server process exited before becoming ready"; \
			$(DUMP_SERVER_LOG); \
			exit 1; \
		fi; \
		if curl -sf "http://127.0.0.1:$(PORT)/entities" >/dev/null 2>&1; then \
			echo "Server ready after ~$$(( i * 200 ))ms."; \
			break; \
		fi; \
		sleep 0.2; \
	done; \
	curl -sf "http://127.0.0.1:$(PORT)/entities" >/dev/null 2>&1 || { \
		echo "ERROR: server did not respond within $(SERVER_READY_TIMEOUT)s"; \
		$(DUMP_SERVER_LOG); \
		exit 1; \
	}

# Shell snippet (expanded inline) that fails fast if $(PORT) is already bound,
# wipes the previous run's logs, installs a cleanup trap, starts the gts server
# in the background on $(PORT) with stdout/stderr redirected to .server.log,
# and waits for readiness via $(WAIT_FOR_SERVER). Binds to 0.0.0.0 so the
# test-runner container can reach the server via host.docker.internal on
# Linux (Docker Desktop on Mac/Windows routes loopback transparently, but the
# Linux docker0 bridge does not — a 127.0.0.1 bind is unreachable from there).
START_SERVER = \
	if lsof -nP -iTCP:$(PORT) -sTCP:LISTEN >/dev/null 2>&1; then \
		echo "ERROR: port $(PORT) is already in use"; \
		lsof -nP -iTCP:$(PORT) -sTCP:LISTEN; \
		exit 1; \
	fi; \
	rm -rf logs; rm -f .server.log; \
	trap 'kill `cat .server.pid 2>/dev/null` 2>/dev/null || true; rm -f .server.pid' INT TERM EXIT; \
	echo "Starting server in background on port $(PORT) (logs: .server.log)..."; \
	./bin/gts server -host 0.0.0.0 -port $(PORT) >.server.log 2>&1 & echo $$! > .server.pid; \
	$(WAIT_FOR_SERVER)

# Shell snippet (expanded inline) that runs the gts-spec test-runner image
# against host.docker.internal:$(PORT). Shared by `gts-spec-tests` (full
# one-shot cycle) and `gts-spec-tests-run` (against an externally-running
# server, paired with `make gts-server`).
RUN_TESTS_DOCKER = \
	echo "Running gts-spec tests via $(GTS_SPEC_REF)..."; \
	docker run --rm \
		--add-host=host.docker.internal:host-gateway \
		$(if $(GTS_SPEC_TESTS_DIR),-v "$(abspath $(GTS_SPEC_TESTS_DIR)):/tests") \
		$(GTS_SPEC_REF) \
		--gts-base-url http://host.docker.internal:$(PORT) \
		$(TEST)

# Pair with `make gts-spec-tests-run TEST=...` for iterative test development.
# Binds to 0.0.0.0 so the test-runner container can reach the server via
# host.docker.internal on Linux (see START_SERVER for the full rationale).
# Run the gts server in the foreground against a freshly built binary
gts-server: build
	./bin/gts server -host 0.0.0.0 -port $(PORT)

# Catches missing/garbled .gts-spec-version before it turns into an opaque
# docker error, by validating $(GTS_SPEC_VERSION) is non-empty and looks
# like a version tag (vMAJOR.MINOR or vMAJOR.MINOR.PATCH).
# Validate the pinned gts-spec version string
gts-spec-version-check:
	@case "$(GTS_SPEC_VERSION)" in \
		"") echo "ERROR: spec version is empty — populate .gts-spec-version or pass GTS_SPEC_VERSION=..."; exit 1 ;; \
		*[!0-9v.]*) echo "ERROR: GTS_SPEC_VERSION='$(GTS_SPEC_VERSION)' — expected 'vMAJOR.MINOR' or 'vMAJOR.MINOR.PATCH'"; exit 1 ;; \
		v[0-9]*.[0-9]*) ;; \
		v[0-9]*.[0-9]*.[0-9]*) ;; \
		*) echo "ERROR: GTS_SPEC_VERSION='$(GTS_SPEC_VERSION)' — expected 'vMAJOR.MINOR' or 'vMAJOR.MINOR.PATCH'"; exit 1 ;; \
	esac

# Skips the network round-trip when the image is already in the local cache
# (safe because the default tag is an immutable vMAJOR.MINOR.PATCH; bumping
# .gts-spec-version produces a new ref, which forces a fresh pull). Use
# `docker rmi $(GTS_SPEC_REF)` to force a refetch of a rolling tag.
# Pull the gts-spec test-runner image from GHCR
gts-spec-tests-pull: gts-spec-version-check
	@docker image inspect $(GTS_SPEC_REF) >/dev/null 2>&1 || docker pull $(GTS_SPEC_REF)

# Run gts-spec conformance suite against an already-running server on $(PORT)
gts-spec-tests-run: gts-spec-tests-pull
	@$(RUN_TESTS_DOCKER)

# Run gts-spec conformance suite (docker) against the local server
gts-spec-tests: build gts-spec-tests-pull
	@set -e; \
	$(START_SERVER); \
	$(RUN_TESTS_DOCKER); \
	echo "gts-spec tests completed successfully"
