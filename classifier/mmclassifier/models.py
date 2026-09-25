from __future__ import annotations

import copy
import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Dict, List, Sequence

from mmclassifier.ensemble import SeedPredictor, aggregate_ensemble

logger = logging.getLogger(__name__)


class ModelNotFoundError(LookupError):
    def __init__(self, name: str) -> None:
        super().__init__(f"Unknown model: {name}")
        self.name = name


@dataclass
class Ensemble:
    name: str
    seeds: List[SeedPredictor]
    device: str

    @property
    def members(self) -> int:
        return len(self.seeds)

    def predict(
        self,
        states: Sequence[Any],
        questions: Dict[str, Any],
        batch_size: int | None = None,
    ) -> List[Dict[str, Any]]:
        return aggregate_ensemble(self.seeds, states, questions, batch_size)


EnsembleLoader = Callable[[str, Path, str], Ensemble]


def load_laya_ensemble(name: str, model_dir: Path, device: str) -> Ensemble:
    import laya
    import torch

    seed_paths = sorted(model_dir.glob("seed*.pt"))
    if not seed_paths:
        raise ModelNotFoundError(name)

    base_agent = laya.load("convaiinnovations/laya", subfolder=None, device=device)
    base_agent.temperature = [1.0, 1.0, 1.0]
    base_agent.temperature_by_options = {}

    seeds: List[SeedPredictor] = []
    for index, path in enumerate(seed_paths):
        agent = base_agent if index == 0 else copy.copy(base_agent)
        if index != 0:
            agent.model = copy.deepcopy(base_agent.model)
        state_dict = torch.load(path, map_location=device)
        agent.model.load_state_dict(state_dict)
        agent.model.eval()
        seeds.append(agent)

    logger.info("Loaded model %r with %d seed(s) on %s", name, len(seeds), device)
    return Ensemble(name=name, seeds=seeds, device=device)


class ModelRegistry:
    def __init__(
        self,
        models_dir: Path,
        device: str,
        loader: EnsembleLoader = load_laya_ensemble,
    ) -> None:
        self._models_dir = Path(models_dir)
        self._device = device
        self._loader = loader
        self._loaded: Dict[str, Ensemble] = {}

    @property
    def device(self) -> str:
        return self._device

    def available_names(self) -> List[str]:
        if not self._models_dir.is_dir():
            return []
        return sorted(entry.name for entry in self._models_dir.iterdir() if entry.is_dir())

    def preload(self, names: Sequence[str]) -> None:
        for name in names:
            self.get(name)

    def get(self, name: str) -> Ensemble:
        if name in self._loaded:
            return self._loaded[name]

        model_dir = self._models_dir / name
        if not model_dir.is_dir():
            raise ModelNotFoundError(name)

        ensemble = self._loader(name, model_dir, self._device)
        self._loaded[name] = ensemble
        return ensemble

    def loaded_models(self) -> Dict[str, int]:
        return {name: ensemble.members for name, ensemble in self._loaded.items()}
