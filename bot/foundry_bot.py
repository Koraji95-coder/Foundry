"""
Foundry Discord Bot — sole operator interface for the Foundry ML pipeline.

Connects to the Foundry broker API and posts results/alerts to Discord channels.
Slash commands are registered to a single guild on startup.
"""

import asyncio
import json
import logging
import os
import sys
from pathlib import Path

try:
    import discord
    from discord import app_commands
    import aiohttp
except ImportError:
    print("Missing dependencies. Install with: pip install -r requirements.txt")
    sys.exit(1)

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("foundry_bot")

CONFIG_PATH = os.environ.get("FOUNDRY_BOT_CONFIG", "bot_config.json")
DEFAULT_BROKER_URL = "http://127.0.0.1:57420"


def load_config() -> dict:
    """Load bot configuration from JSON file."""
    if not os.path.exists(CONFIG_PATH):
        logger.warning("Config file %s not found. Using defaults.", CONFIG_PATH)
        return {}
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


config = load_config()
BROKER_URL = config.get("broker_url", DEFAULT_BROKER_URL)
TOKEN = config.get("token") or os.environ.get("FOUNDRY_DISCORD_TOKEN")
GUILD_ID = config.get("guild_id") or os.environ.get("FOUNDRY_BOT_GUILD_ID", "0")

intents = discord.Intents.default()
bot = discord.Client(intents=intents)
tree = app_commands.CommandTree(bot)
guild = discord.Object(id=int(GUILD_ID))
http_session: aiohttp.ClientSession | None = None

REPO_ROOT = os.environ.get("FOUNDRY_REPO_ROOT", str(Path(__file__).parent.parent))
MAX_DISCORD_OUTPUT_LENGTH = 1900

if GUILD_ID == "0":
    logger.warning("No guild_id configured. Set guild_id in bot_config.json or FOUNDRY_BOT_GUILD_ID env var.")


@bot.event
async def on_ready():
    global http_session
    if http_session is None or http_session.closed:
        http_session = aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=10))
    await tree.sync(guild=guild)
    logger.info("Foundry bot connected as %s — slash commands synced", bot.user)


@tree.command(name="health", description="Check Foundry broker health", guild=guild)
async def health(interaction: discord.Interaction):
    try:
        async with http_session.get(f"{BROKER_URL}/health") as resp:
            data = await resp.json()
        await interaction.response.send_message(f"**Foundry Health**: {data.get('status', 'unknown')}")
    except Exception as e:
        await interaction.response.send_message(f"❌ Health check failed: {e}")


@tree.command(name="status", description="Get Foundry pipeline status", guild=guild)
async def status(interaction: discord.Interaction):
    try:
        async with http_session.get(f"{BROKER_URL}/api/state") as resp:
            data = await resp.json()
        ml = data.get("ml", {})
        await interaction.response.send_message(
            f"**ML Pipeline**: {'Enabled' if ml.get('enabled') else 'Disabled'}\n"
            f"**Summary**: {ml.get('summary', 'N/A')}"
        )
    except Exception as e:
        await interaction.response.send_message(f"❌ Status check failed: {e}")


PIPELINE_CHOICES = [
    app_commands.Choice(name="pipeline", value="pipeline"),
    app_commands.Choice(name="embeddings", value="embeddings"),
    app_commands.Choice(name="export", value="export"),
    app_commands.Choice(name="index", value="index"),
]


@tree.command(name="run", description="Trigger an ML pipeline run", guild=guild)
@app_commands.describe(pipeline_type="Pipeline type to run")
@app_commands.choices(pipeline_type=PIPELINE_CHOICES)
async def run_pipeline(interaction: discord.Interaction, pipeline_type: app_commands.Choice[str] = None):
    selected = pipeline_type.value if pipeline_type else "pipeline"
    endpoint_map = {
        "pipeline": "/api/ml/pipeline",
        "embeddings": "/api/ml/embeddings",
        "export": "/api/ml/export-artifacts",
        "index": "/api/ml/index-knowledge",
    }
    endpoint = endpoint_map.get(selected)
    if not endpoint:
        await interaction.response.send_message(
            f"Unknown pipeline type: `{selected}`. Use: {', '.join(endpoint_map.keys())}"
        )
        return

    try:
        async with http_session.post(f"{BROKER_URL}{endpoint}") as resp:
            data = await resp.json()
        job_id = data.get("jobId", "N/A")
        await interaction.response.send_message(f"✅ Job queued: `{job_id}` (type: {selected})")
    except Exception as e:
        await interaction.response.send_message(f"❌ Failed to trigger {selected}: {e}")


@tree.command(name="jobs", description="List recent jobs", guild=guild)
async def list_jobs(interaction: discord.Interaction):
    try:
        async with http_session.get(f"{BROKER_URL}/api/jobs") as resp:
            data = await resp.json()
        jobs = data.get("jobs", [])[:5]
        if not jobs:
            await interaction.response.send_message("No recent jobs.")
            return
        lines = [f"• `{j['id'][:8]}` — {j['type']} — **{j['status']}**" for j in jobs]
        await interaction.response.send_message("**Recent Jobs:**\n" + "\n".join(lines))
    except Exception as e:
        await interaction.response.send_message(f"❌ Failed to list jobs: {e}")


async def run_script(
    interaction: discord.Interaction,
    script_name: str,
    description: str,
    script_dir: str = "automation",
    extra_args: list[str] | None = None,
):
    """Run a PowerShell script as an async subprocess with deferred interaction."""
    await interaction.response.defer()
    script_path = os.path.join(REPO_ROOT, "scripts", script_dir, script_name)
    cmd = ["pwsh", "-NoProfile", "-File", script_path] + (extra_args or [])
    try:
        process = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
            cwd=REPO_ROOT,
        )
        stdout, stderr = await asyncio.wait_for(process.communicate(), timeout=300)
        output = stdout.decode()[-MAX_DISCORD_OUTPUT_LENGTH:] if stdout else "No output"
        await interaction.followup.send(f"**{description} complete:**\n```\n{output}\n```")
    except asyncio.TimeoutError:
        await interaction.followup.send(f"❌ {description} timed out after 5 minutes")
    except Exception as e:
        await interaction.followup.send(f"❌ {description} failed: {e}")


@tree.command(name="scan", description="Run issue pipeline — creates issues and assigns to Copilot", guild=guild)
async def scan(interaction: discord.Interaction):
    await run_script(interaction, "auto-issue-pipeline.ps1", "Issue scan")


@tree.command(name="review", description="Score all open PRs with the scoring engine", guild=guild)
async def review(interaction: discord.Interaction):
    await run_script(interaction, "auto-pr-review.ps1", "PR review")


@tree.command(name="approve", description="Approve a PR and log the decision", guild=guild)
@app_commands.describe(pr_ref="PR reference (e.g. Foundry#27 or Suite#54)")
async def approve(interaction: discord.Interaction, pr_ref: str):
    await run_script(interaction, "approve.ps1", "Approve", script_dir="commands", extra_args=[pr_ref])


@tree.command(name="reject", description="Reject a PR and log the decision", guild=guild)
@app_commands.describe(pr_ref="PR reference (e.g. Foundry#31 or Suite#57)", reason="Reason for rejection")
async def reject(interaction: discord.Interaction, pr_ref: str, reason: str = "No reason given"):
    await run_script(interaction, "reject.ps1", "Reject", script_dir="commands", extra_args=[pr_ref, reason])


@tree.command(name="triage", description="Group overlapping PRs and recommend merge order", guild=guild)
async def triage(interaction: discord.Interaction):
    await interaction.response.defer()
    try:
        # Fetch all open PRs from GitHub API
        gh_headers = {"Authorization": f"Bearer {os.environ.get('GITHUB_TOKEN', '')}", "Accept": "application/vnd.github.v3+json"}
        repo = "Koraji95-coder/Foundry"

        async with http_session.get(f"https://api.github.com/repos/{repo}/pulls?state=open&per_page=100", headers=gh_headers) as resp:
            prs = await resp.json()

        if not prs:
            await interaction.followup.send(embed=discord.Embed(title="Triage", description="No open PRs.", color=0x3498db))
            return

        # Fetch file lists for each PR
        pr_data = {}
        for pr in prs:
            try:
                async with http_session.get(f"https://api.github.com/repos/{repo}/pulls/{pr['number']}/files?per_page=100", headers=gh_headers) as resp:
                    files_resp = await resp.json()
                pr_data[pr['number']] = {
                    "title": pr['title'],
                    "files": [f['filename'] for f in files_resp],
                    "additions": pr.get('additions', 0),
                    "deletions": pr.get('deletions', 0),
                }
            except Exception:
                continue

        # Try to get scores from the latest review data
        pr_scores = {}
        try:
            state_root = os.environ.get("FOUNDRY_STATE_ROOT", os.path.expanduser("~/FoundryState"))
            raw_path = os.path.join(state_root, "raw.jsonl")
            if os.path.exists(raw_path):
                import json as json_mod
                with open(raw_path, "r") as f:
                    for line in f:
                        try:
                            record = json_mod.loads(line.strip())
                            if record.get("pr_number") and record.get("score"):
                                pr_scores[record["pr_number"]] = record["score"]
                        except Exception:
                            continue
        except Exception:
            pass

        # Build conflict graph — PRs that share files are connected
        clusters = []
        assigned = set()

        pr_numbers = list(pr_data.keys())
        for i, pr_a in enumerate(pr_numbers):
            if pr_a in assigned:
                continue
            cluster = [pr_a]
            assigned.add(pr_a)
            files_a = set(pr_data[pr_a]["files"])

            for pr_b in pr_numbers[i+1:]:
                if pr_b in assigned:
                    continue
                files_b = set(pr_data[pr_b]["files"])
                overlap = files_a & files_b
                if overlap:
                    cluster.append(pr_b)
                    assigned.add(pr_b)
                    files_a |= files_b  # expand cluster's file set

            if len(cluster) > 1:
                clusters.append(cluster)

        # Build the triage embed
        if not clusters:
            # No conflicts — just list all PRs sorted by score
            sorted_prs = sorted(pr_data.keys(), key=lambda n: pr_scores.get(n, 0), reverse=True)
            lines = []
            for n in sorted_prs[:15]:
                score = pr_scores.get(n, "?")
                size = pr_data[n]["additions"] + pr_data[n]["deletions"]
                lines.append(f"#{n} — score {score}/10 — {size} lines — {pr_data[n]['title'][:60]}")
            embed = discord.Embed(title="Triage — no conflicts", description="No file overlaps detected. Merge in any order.", color=0x2ecc71)
            embed.add_field(name="By score (highest first)", value="\n".join(lines) or "No PRs", inline=False)
            await interaction.followup.send(embed=embed)
            return

        # Format conflict clusters with recommended merge order
        embed = discord.Embed(title=f"Triage — {len(clusters)} conflict cluster{'s' if len(clusters) != 1 else ''}", color=0xf39c12)

        for i, cluster in enumerate(clusters):
            # Sort: highest score first, smallest size as tiebreaker
            sorted_cluster = sorted(cluster, key=lambda n: (-pr_scores.get(n, 0), pr_data[n]["additions"] + pr_data[n]["deletions"]))

            lines = []
            for rank, n in enumerate(sorted_cluster):
                score = pr_scores.get(n, "?")
                size = pr_data[n]["additions"] + pr_data[n]["deletions"]
                prefix = f"{rank+1}."
                label = " (merge first)" if rank == 0 else " (re-review after)"
                lines.append(f"{prefix} #{n} — score {score}/10 — {size} lines{label}")

            # Find shared files
            all_files = set()
            for n in cluster:
                all_files |= set(pr_data[n]["files"])
            shared = set()
            for n in cluster:
                shared |= set(pr_data[n]["files"]) & all_files

            embed.add_field(
                name=f"Cluster {i+1}: {len(cluster)} PRs",
                value="\n".join(lines) + f"\n*Shared files: {', '.join(sorted(shared)[:5])}{'...' if len(shared) > 5 else ''}*",
                inline=False
            )

        # Add unconnected PRs
        unconnected = [n for n in pr_numbers if n not in assigned]
        if unconnected:
            sorted_unc = sorted(unconnected, key=lambda n: pr_scores.get(n, 0), reverse=True)
            lines = [f"#{n} — score {pr_scores.get(n, '?')}/10 — {pr_data[n]['title'][:50]}" for n in sorted_unc[:10]]
            embed.add_field(name="No conflicts (merge anytime)", value="\n".join(lines), inline=False)

        await interaction.followup.send(embed=embed)
    except Exception as e:
        logger.exception("Triage failed")
        await interaction.followup.send(embed=discord.Embed(title="Triage failed", description=str(e)[:200], color=0xe74c3c))


@tree.command(name="commands", description="List all Foundry bot commands", guild=guild)
async def list_commands(interaction: discord.Interaction):
    embed = discord.Embed(title="Foundry commands", color=0x3498DB)
    embed.add_field(name="/health", value="Check broker health", inline=False)
    embed.add_field(name="/status", value="Get ML pipeline status", inline=False)
    embed.add_field(name="/run [type]", value="Trigger ML pipeline (pipeline, embeddings, export, index)", inline=False)
    embed.add_field(name="/jobs", value="List recent jobs", inline=False)
    embed.add_field(name="/scan", value="Run issue pipeline — creates issues and assigns to Copilot", inline=False)
    embed.add_field(name="/review", value="Score all open PRs with the scoring engine", inline=False)
    embed.add_field(name="/approve [pr_ref]", value="Approve a PR and log the decision", inline=False)
    embed.add_field(name="/reject [pr_ref] [reason]", value="Reject a PR and log the decision", inline=False)
    embed.add_field(name="/triage", value="Group overlapping PRs and recommend merge order", inline=False)
    embed.add_field(name="/commands", value="List all Foundry bot commands", inline=False)
    await interaction.response.send_message(embed=embed)


if __name__ == "__main__":
    if not TOKEN:
        logger.error("No Discord token configured. Set FOUNDRY_DISCORD_TOKEN or add 'token' to bot_config.json.")
        sys.exit(1)
    bot.run(TOKEN)
